import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
from unittest.mock import patch


spec = importlib.util.spec_from_file_location("deploy_aws", Path(__file__).with_name("deploy-aws.py"))
deploy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deploy)


class DeploymentTests(unittest.TestCase):
    def test_hostname_cannot_inject_caddy_configuration(self):
        for value in ["https://npc.example.com", "example.com {", "example.com\n}", "*.example.com", "localhost", "127.0.0.1"]:
            with self.subTest(value=value), self.assertRaises(ValueError):
                deploy.hostname(value)
        self.assertEqual(deploy.hostname("NPC.example.com"), "npc.example.com")

    def test_env_values_remain_literal(self):
        with self.assertRaises(ValueError):
            deploy.env_value("secret\nAUTH_MODE=local")

    @unittest.skipUnless(shutil.which("docker"), "Docker Compose required")
    def test_secrets_reach_container_unchanged(self):
        values = ["a$TOKEN#b'c\\d", "ends\\", "back\\'quote", 'double"quote', "$(id)", "with spaces"]
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "compose.yaml").write_text("services:\n  app:\n    image: node:22-bookworm-slim\n    env_file: .env\n")
            (root / ".env").write_text("".join(f"SECRET_{i}={deploy.env_value(value)}\n" for i, value in enumerate(values)))
            try:
                result = subprocess.run(["docker", "compose", "run", "--rm", "-T", "app", "node", "-e",
                                         "console.log(JSON.stringify(Object.fromEntries(Object.entries(process.env).filter(([k])=>k.startsWith('SECRET_')))))"],
                                        cwd=root, check=True, capture_output=True, text=True)
                self.assertEqual(json.loads(result.stdout), {f"SECRET_{i}": value for i, value in enumerate(values)})
            finally:
                subprocess.run(["docker", "compose", "down"], cwd=root, check=True, capture_output=True)

    def test_settings_survive_redeployment_without_prompting(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".env").write_text("existing-secret")
            (root / "scopes.local.json").write_text("existing-grants")
            deploy.configure(root)
            self.assertEqual((root / ".env").read_text(), "existing-secret")
            self.assertEqual((root / "scopes.local.json").read_text(), "existing-grants")

    def test_existing_env_needs_no_grants_file(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / ".env").write_text("existing-secret")
            deploy.configure(root)
            self.assertEqual((root / ".env").read_text(), "existing-secret")

    def test_first_install_only_requests_app_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            with patch('builtins.input', side_effect=['123']), patch('getpass.getpass', side_effect=['app-secret','model-key']):
                deploy.configure(root)
            self.assertIn('META_APP_ID="123"', (root / '.env').read_text())
            self.assertFalse((root / 'scopes.local.json').exists())


if __name__ == "__main__":
    unittest.main()
