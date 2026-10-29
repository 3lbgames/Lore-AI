using System;
using UnityEngine;

public class NpcSpeechOutput : MonoBehaviour
{
    public string Text { get; private set; }
    public event Action Changed;

    public void SetText(string text)
    {
        Text = text;
        Changed?.Invoke();
    }

    public void Clear() => SetText(null);
    private void OnDisable() => Clear();
}
