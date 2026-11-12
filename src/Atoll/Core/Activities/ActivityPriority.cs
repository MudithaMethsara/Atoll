namespace Atoll.Core.Activities;

public enum ActivityPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum ActivityKind
{
    Generic,
    Media,
    Timer,
    Call,
    Notification,
    Battery,
    Bluetooth,
    Osd,
    Privacy,
    Calendar,
    FileShelf,
    QuickActions,
    Glance
}

