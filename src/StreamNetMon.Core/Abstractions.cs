namespace StreamNetMon.Core;

public interface IClock
{
    DateTimeOffset Now { get; }
}
