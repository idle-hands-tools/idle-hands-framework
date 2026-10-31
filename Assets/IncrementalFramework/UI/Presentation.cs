using System;

namespace IncrementalFramework.UI
{
    /// <summary>A temporary presentation overlay. True permits authored eligibility; it never bypasses it.</summary>
    public readonly struct UiPresentation
    {
        public bool? Enabled { get; }
        public bool? Visible { get; }
        public bool? Highlighted { get; }
        public UiColor? HighlightColor { get; }
        public float? HighlightWidth { get; }
        public UiPresentation(bool? enabled=null, bool? visible=null, bool? highlighted=null,
            UiColor? highlightColor=null, float? highlightWidth=null)
        { Enabled=enabled; Visible=visible; Highlighted=highlighted; HighlightColor=highlightColor; HighlightWidth=highlightWidth; }
    }
    public interface IUiPresentationPolicy
    {
        event Action Changed;
        UiPresentation Resolve(string targetId);
    }
}
