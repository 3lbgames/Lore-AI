namespace Core3lb
{
    public interface ISpeechCaptureStatus
    {
        bool IsRecording { get; }
        bool IsProcessing { get; }
        string PartialTranscript { get; }
    }

}
