using System.IO;
using Atoll.Modules.Shelf;
using Xunit;

namespace Atoll.Tests;

public sealed class FileShelfModuleTests
{
    [Fact]
    public void FileShelf_AddDeduplicateRemoveAndClear_WorksEndToEnd()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"atoll_shelf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string settingsPath = Path.Combine(tempDir, "settings.json");
        string fileA = Path.Combine(tempDir, "notes.txt");
        string fileB = Path.Combine(tempDir, "image.png");
        File.WriteAllText(fileA, "hello world");
        File.WriteAllBytes(fileB, new byte[2048]);

        try
        {
            StaTestHelper.Run(() =>
            {
                var ctx = StaTestHelper.CreateTestModuleContext(settingsPath);
                using var shelf = new FileShelfModule();
                shelf.InitializeAsync(ctx).GetAwaiter().GetResult();

                shelf.AddFiles([fileA, fileA, fileB, Path.Combine(tempDir, "missing.txt")]);

                Assert.True(shelf.IsActive);
                Assert.Equal(2, shelf.Items.Count);
                Assert.Equal(2, shelf.GetExistingPaths().Length);

                var activity = Assert.IsType<FileShelfActivity>(ctx.ActivityManager.PrimaryActivity);
                Assert.Equal("2", activity.CompactTrailingText);

                shelf.RemoveFile(fileA);
                Assert.Single(shelf.Items);
                Assert.Equal("1", activity.CompactTrailingText);

                shelf.Clear();
                Assert.False(shelf.IsActive);
                Assert.Empty(shelf.Items);
                Assert.Null(ctx.ActivityManager.PrimaryActivity);

                ctx.AudioService.Dispose();
                ctx.BrightnessService.Dispose();
            });
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

