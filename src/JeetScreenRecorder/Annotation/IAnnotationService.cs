namespace JeetScreenRecorder.Annotation;

public enum AnnotationTool
{
    Pen, Highlighter, Arrow, Line, Rectangle, Circle, FilledRectangle, Text,
    NumberMarker, LaserPointer, Spotlight, Blur, Eraser
}

public interface IAnnotationService
{
    void ShowToolbar();
    void HideToolbar();
    void SetTool(AnnotationTool tool);
    void Undo();
    void Redo();
    void ClearAll();
}
