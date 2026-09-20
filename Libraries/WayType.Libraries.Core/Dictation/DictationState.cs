namespace WayType.Libraries.Core.Dictation;

public enum DictationState
{
    Idle,
    Listening,
    Transcribing,
    PostProcessing,
    Injecting,
    Error,
}
