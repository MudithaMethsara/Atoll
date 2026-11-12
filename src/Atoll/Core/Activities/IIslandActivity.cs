using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Atoll.Core.Activities;

public interface IIslandActivity : INotifyPropertyChanged
{
    string Id { get; }
    string ModuleId { get; }
    ActivityKind Kind { get; }
    ActivityPriority Priority { get; }
    bool SupportsMinimal { get; }
    bool SupportsExpanded { get; }
    bool IsAlertDismissible { get; }

    string Title { get; }
    string Subtitle { get; }
    string CompactTrailingText { get; }
    string Glyph { get; }
    Color AccentColor { get; }
    ImageSource? Thumbnail { get; }
    double? Progress { get; }

    Size PreferredCompactSize { get; }
    Size PreferredExpandedSize { get; }
    Size PreferredAlertSize { get; }

    DateTime CreatedAt { get; }

    void OnActivated();
    void OnDismissed();
}

