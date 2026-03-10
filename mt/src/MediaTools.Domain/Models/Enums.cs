namespace MediaTools.Domain.Models;

public enum PipelineStep
{
    Handbrake,
    NormalizeAudio,
    Promote
}

public enum Kind
{
    Tv,
    Movies,
    Clips
}

public enum TargetMode
{
    Dir,
    File
}

public enum Verbosity
{
    Quiet,
    Normal,
    Verbose
}
