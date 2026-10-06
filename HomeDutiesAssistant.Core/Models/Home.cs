namespace HomeDutiesAssistant.Models;

// A Home is the tenant/container that owns its users, duties, and tasks.
public sealed class Home
{
    public long Id { get; set; } // 0 == not yet saved
    public string Name { get; set; } = "";
}

// Per-home caps.
public static class HomeLimits
{
    public const int MaxDuties = 1000;
    public const int MaxTasks = 1000;
}