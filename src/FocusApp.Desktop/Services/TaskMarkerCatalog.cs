using System.Windows.Media;
using FocusApp.Core;

namespace FocusApp.Desktop.Services;

public sealed record TaskMarker(string Id, int Row, string Label)
{
    public string FileName => Id + ".svg";
    public ImageSource Icon => TaskMarkerIconLoader.Load(FileName);
}

public static class TaskMarkerCatalog
{
    private static readonly string[] Labels =
    [
        "数字 1", "数字 2", "数字 3", "数字 4", "数字 5",
        "红色旗帜", "橙色旗帜", "黄色旗帜", "绿色旗帜", "蓝色旗帜",
        "红色圆点", "橙色圆点", "黄色圆点", "绿色圆点", "蓝色圆点",
        "感叹号", "问号", "播放", "暂停", "完成"
    ];
    public static IReadOnlyList<TaskMarker> All { get; } = Array.AsReadOnly(
        TaskMarkers.All.Select((id, index) => new TaskMarker(id, index / 5, Labels[index])).ToArray());
    public static TaskMarker? Find(string? id)
    {
        id = TaskFlagColors.Normalize(id);
        // The old fifth flag was gray; it now occupies the blue fifth slot in the new library.
        id = id switch { "red" => "flag_1", "orange" => "flag_2", "yellow" => "flag_3", "green" => "flag_4", "gray" => "flag_5", _ => id };
        return All.FirstOrDefault(marker => marker.Id == id);
    }
}
