namespace Akka.Persistence.EventStore.Benchmarks;

internal static class Const
{
    public const int TotalMessages = 3000000;
    public const int TagLowerBound = 2 * (TotalMessages / 3);
    public const string Tag10 = "Tag1";
    public const string Tag100 = "Tag2";
    public const string Tag1000 = "Tag3";
    public const string Tag10000 = "Tag4";
    public const int Tag10UpperBound = TagLowerBound + 10;
    public const int Tag100UpperBound = TagLowerBound + 100;
    public const int Tag1000UpperBound = TagLowerBound + 1000;
    public const int Tag10000UpperBound = TagLowerBound + 10000;
}
