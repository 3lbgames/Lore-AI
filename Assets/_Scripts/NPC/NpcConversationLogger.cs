using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Records conversations between the player and NPCs. Every conversation in one play session goes into a
/// single shared file, "Conversation Log <yyyy-MM-dd> <HH-mm-ss>.txt" (named for when the session started).
/// Each conversation is a block headed by the time it began, with a blank line between blocks:
///
///   14:32:05
///   Mira: Hello, good to see you!
///   Player: Hello to you, how are you?
///
///   14:35:40
///   Flynn: What do you want?
///
/// Lines are written the moment they happen, so nothing is lost if the game stops mid-conversation.
/// The file is only created once someone actually speaks.
/// </summary>
[RequireComponent(typeof(NpcCharacterDirector), typeof(NpcMemory))]
public class NpcConversationLogger : MonoBehaviour
{
    [Tooltip("Name shown before this NPC's lines. Leave empty to use the profile's npcId, capitalized (\"mira\" -> \"Mira\").")]
    public string displayName;
    public string playerName = "Player";
    [Tooltip("Log folder name. In the editor it sits in the project root next to Assets; in builds it goes under Application.persistentDataPath. All NPCs share one session file, so keep this the same on every NPC.")]
    public string folderName = "ConversationLogs";
    [Tooltip("Also log NPC lines whose speech failed to play (the player never heard them).")]
    public bool includeFailedSpeech;

    // ---- Shared by every NPC: one log file per play session ----
    static DateTime sessionStartedAt = DateTime.Now;
    static string sessionLogPath;
    static NpcConversationLogger lastWriter;

    /// <summary>Path of this play session's log file, or null until the first line is written.</summary>
    public static string SessionLogPath => sessionLogPath;

    // Runs at the start of every play session, including when domain reload is disabled
    // in Enter Play Mode settings (where static fields would otherwise carry over).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        sessionStartedAt = DateTime.Now;
        sessionLogPath = null;
        lastWriter = null;
    }

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
    DateTime conversationStartedAt;
    bool conversationActive;
    bool headerWritten;

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
        conversationStartedAt = DateTime.Now;
        conversationActive = true;
        headerWritten = false; // the time header is written with the first line
    }

    void EndConversation()
    {
        conversationActive = false;
        headerWritten = false;
        if (lastWriter == this) lastWriter = null;
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
            if (sessionLogPath == null)
            {
                Directory.CreateDirectory(LogFolder);
                sessionLogPath = UniquePath(LogFolder, $"Conversation Log {sessionStartedAt:yyyy-MM-dd} {sessionStartedAt:HH-mm-ss}");
            }

            var entry = new StringBuilder();
            // Start a new block when this conversation begins, or when it resumes after another
            // NPC's conversation wrote in between, so blocks never mix speakers from different chats.
            if (!headerWritten || lastWriter != this)
            {
                if (File.Exists(sessionLogPath) && new FileInfo(sessionLogPath).Length > 0)
                    entry.Append(Environment.NewLine);
                DateTime stamp = headerWritten ? DateTime.Now : conversationStartedAt;
                entry.Append(stamp.ToString("HH:mm:ss")).Append(Environment.NewLine);
                headerWritten = true;
            }
            entry.Append(speaker).Append(": ").Append(Flatten(text)).Append(Environment.NewLine);

            File.AppendAllText(sessionLogPath, entry.ToString());
            lastWriter = this;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[{nameof(NpcConversationLogger)}] Couldn't write conversation log: {e.Message}", this);
        }
    }

    static string Flatten(string text) => text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();

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
