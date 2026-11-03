using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Records each conversation between the player and this NPC to a .txt file named
/// "<NpcName> <yyyy-MM-dd> <HH-mm-ss>.txt", using the time the conversation began.
/// Each line is written as "Speaker: text" the moment it happens, so nothing is lost
/// if the game stops mid-conversation. A file is only created once someone actually speaks.
/// </summary>
[RequireComponent(typeof(NpcCharacterDirector), typeof(NpcMemory))]
public class NpcConversationLogger : MonoBehaviour
{
    [Tooltip("Name used in the file title and before NPC lines. Leave empty to use the profile's npcId, capitalized (\"mira\" -> \"Mira\").")]
    public string displayName;
    public string playerName = "Player";
    [Tooltip("Log folder name. In the editor it sits in the project root next to Assets; in builds it goes under Application.persistentDataPath.")]
    public string folderName = "ConversationLogs";
    [Tooltip("Also log NPC lines whose speech failed to play (the player never heard them).")]
    public bool includeFailedSpeech;

    public string CurrentLogPath { get; private set; }
    /// <summary>
    /// In the editor: "<Project>/ConversationLogs", next to Assets (not inside it, so Unity won't import the logs).
    /// In a build there is no project folder, and writing inside the app is unreliable (on macOS it sits inside
    /// the signed .app bundle), so builds fall back to Application.persistentDataPath.
    /// </summary>
    public string LogFolder
    {
        get
        {
#if UNITY_EDITOR
            string root = Directory.GetParent(Application.dataPath).FullName;
#else
            string root = Application.persistentDataPath;
#endif
            return Path.Combine(root, folderName);
        }
    }

    NpcCharacterDirector director;
    NpcMemory memory;
    DateTime startedAt;
    bool conversationActive;

    void Awake()
    {
        director = GetComponent<NpcCharacterDirector>();
        memory = GetComponent<NpcMemory>();
    }

    void OnEnable()
    {
        director.InteractionStarted += BeginConversation;
        director.InteractionEnded += EndConversation;
        memory.PlayerLineCommitted += OnPlayerLine;
        memory.NpcLineRecorded += OnNpcLine;
    }

    void OnDisable()
    {
        director.InteractionStarted -= BeginConversation;
        director.InteractionEnded -= EndConversation;
        memory.PlayerLineCommitted -= OnPlayerLine;
        memory.NpcLineRecorded -= OnNpcLine;
        EndConversation();
    }

    void BeginConversation()
    {
        startedAt = DateTime.Now;
        CurrentLogPath = null; // created lazily on the first line
        conversationActive = true;
    }

    void EndConversation()
    {
        conversationActive = false;
        CurrentLogPath = null;
    }

    void OnPlayerLine(string text) => WriteLine(playerName, text);

    void OnNpcLine(string text, string outcome)
    {
        // "Completed" = fully spoken, "Cancelled" = player interrupted (still partly heard).
        if (outcome != "Completed" && outcome != "Cancelled" && !includeFailedSpeech) return;
        WriteLine(NpcName, text);
    }

    string NpcName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(displayName)) return displayName.Trim();
            string id = director && director.profile ? director.profile.npcId : null;
            if (string.IsNullOrWhiteSpace(id)) return gameObject.name;
            id = id.Trim();
            return char.ToUpperInvariant(id[0]) + id.Substring(1);
        }
    }

    void WriteLine(string speaker, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (!conversationActive) BeginConversation();
        try
        {
            if (CurrentLogPath == null)
            {
                Directory.CreateDirectory(LogFolder);
                CurrentLogPath = UniquePath(LogFolder, $"{SafeFileName(NpcName)} {startedAt:yyyy-MM-dd} {startedAt:HH-mm-ss}");
            }
            File.AppendAllText(CurrentLogPath, $"{speaker}: {Flatten(text)}{Environment.NewLine}");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[{nameof(NpcConversationLogger)}] Couldn't write conversation log: {e.Message}", this);
        }
    }

    static string Flatten(string text) => text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

    // Characters that are invalid in file names on Windows, macOS or Linux. Using one fixed
    // set (rather than the current OS's list) keeps names valid when logs move between machines.
    static readonly char[] UnsafeFileNameChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };

    static string SafeFileName(string name)
    {
        var chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (char.IsControl(chars[i]) || Array.IndexOf(UnsafeFileNameChars, chars[i]) >= 0) chars[i] = '_';
        // Windows also rejects names ending in a dot or space.
        return new string(chars).TrimEnd('.', ' ');
    }

    static string UniquePath(string folder, string baseName)
    {
        string path = Path.Combine(folder, baseName + ".txt");
        for (int i = 2; File.Exists(path); i++) path = Path.Combine(folder, $"{baseName} ({i}).txt");
        return path;
    }

    [ContextMenu("Open Log Folder")]
    void OpenLogFolder()
    {
        Directory.CreateDirectory(LogFolder);
#if UNITY_EDITOR
        // Opens Explorer on Windows and Finder on macOS.
        UnityEditor.EditorUtility.RevealInFinder(LogFolder);
#else
        Application.OpenURL(new Uri(LogFolder).AbsoluteUri);
#endif
    }
}
