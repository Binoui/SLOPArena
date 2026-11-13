#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SlopArena.Client.Workshop
{
    public sealed class WorkshopPublishEdits
    {
        public string? Title { get; }
        public string? Description { get; }
        public int? Visibility { get; }
        public bool UpdatePreview { get; private set; }
        public bool UpdateTags { get; private set; }
        public WorkshopPublishEdits Assets(bool preview, bool tags)
        { UpdatePreview = preview; UpdateTags = tags; return this; }
        public static bool AssetChanged(string? submitted, string current) => !string.IsNullOrEmpty(submitted) && submitted != current;
        public static WorkshopPublishEdits ForItem(bool submitted, string beforeTitle, string beforeDescription, int beforeVisibility, string title, string description, int visibility)
            => submitted ? Changed(beforeTitle, beforeDescription, beforeVisibility, title, description, visibility) : new(title, description, visibility);
        public void RequireFirstSubmit(bool submitted)
        { if (!submitted && (Title == null || Description == null || !Visibility.HasValue)) throw new InvalidOperationException("The first successful submission requires title, description and visibility."); }
        public WorkshopPublishEdits(string? title, string? description, int? visibility)
        { Title = title; Description = description; Visibility = visibility; }
        public static void RequireOwner(ulong owner, ulong user)
        { if (owner != user) throw new WorkshopItemUnavailableException(); }
        public static WorkshopPublishEdits Changed(string titleBefore, string descriptionBefore, int visibilityBefore,
            string title, string description, int visibility) => new(title == titleBefore ? null : title,
                description == descriptionBefore ? null : description, visibility == visibilityBefore ? (int?)null : visibility);
    }
    public sealed class WorkshopItemUnavailableException : InvalidOperationException
    {
        public WorkshopItemUnavailableException() : base("This Steam item is gone or is not yours. You can publish this local model as a new item.") { }
    }
    public sealed class WorkshopOperations<T>
    {
        private readonly Dictionary<string, Task<T>> _running = new();
        public bool IsRunning(string key)
        { lock (_running) return _running.TryGetValue(key, out var task) && !task.IsCompleted; }
        public Task<T> Start(string key, Func<Task<T>> run)
        {
            lock (_running)
            {
                if (_running.TryGetValue(key, out var task) && !task.IsCompleted) return task;
                return _running[key] = run();
            }
        }
    }
}
