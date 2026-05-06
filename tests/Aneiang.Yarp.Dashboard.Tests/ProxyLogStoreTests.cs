using Aneiang.Yarp.Dashboard.Models;
using Aneiang.Yarp.Dashboard.Services;
using FluentAssertions;
using Xunit;

namespace Aneiang.Yarp.Dashboard.Tests;

public class ProxyLogStoreTests
{
    [Fact]
    public void Add_ShouldStoreLogEntry()
    {
        // Arrange
        var store = new ProxyLogStore(10);
        var entry = new LogEntry
        {
            Timestamp = DateTime.UtcNow,
            Level = "Information",
            Message = "Test log entry"
        };

        // Act
        store.Add(entry);
        var logs = store.GetRecent(10);

        // Assert
        logs.Should().HaveCount(1);
        logs[0].Message.Should().Be("Test log entry");
    }

    [Fact]
    public void Add_ShouldEvictOldestWhenCapacityExceeded()
    {
        // Arrange
        var store = new ProxyLogStore(3);
        
        store.Add(new LogEntry { Message = "Log 1" });
        store.Add(new LogEntry { Message = "Log 2" });
        store.Add(new LogEntry { Message = "Log 3" });
        store.Add(new LogEntry { Message = "Log 4" });

        // Act
        var logs = store.GetRecent(10);

        // Assert
        logs.Should().HaveCount(3);
        logs[0].Message.Should().Be("Log 2");
        logs[1].Message.Should().Be("Log 3");
        logs[2].Message.Should().Be("Log 4");
    }

    [Fact]
    public void GetRecent_ShouldReturnLimitedEntries()
    {
        // Arrange
        var store = new ProxyLogStore(10);
        
        for (int i = 0; i < 20; i++)
        {
            store.Add(new LogEntry { Message = $"Log {i}" });
        }

        // Act
        var logs = store.GetRecent(5);

        // Assert
        logs.Should().HaveCount(5);
        logs[0].Message.Should().Be("Log 15");
        logs[4].Message.Should().Be("Log 19");
    }

    [Fact]
    public void Clear_ShouldRemoveAllEntries()
    {
        // Arrange
        var store = new ProxyLogStore(10);
        
        store.Add(new LogEntry { Message = "Log 1" });
        store.Add(new LogEntry { Message = "Log 2" });

        // Act
        store.Clear();
        var logs = store.GetRecent(10);

        // Assert
        logs.Should().BeEmpty();
    }

    [Fact]
    public void Add_ShouldBeThreadSafe()
    {
        // Arrange
        var store = new ProxyLogStore(100);
        var tasks = new List<Task>();

        // Act - Add 1000 entries concurrently
        for (int i = 0; i < 1000; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                store.Add(new LogEntry { Message = $"Log {index}" });
            }));
        }

        Task.WaitAll(tasks.ToArray());
        var logs = store.GetRecent(1000);

        // Assert
        logs.Should().HaveCount(100);
    }
}
