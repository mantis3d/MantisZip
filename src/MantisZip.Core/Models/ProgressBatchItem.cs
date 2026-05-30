using System.ComponentModel;

namespace MantisZip.Core.Models;

/// <summary>
/// 批量处理项的状态
/// </summary>
public enum BatchItemStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}

/// <summary>
/// 批量处理列表中的单个项（如压缩/解压任务）
/// </summary>
public class BatchItem : INotifyPropertyChanged
{
    /// <summary>显示名称（如文件名或任务名）</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>完整路径</summary>
    public string? FullPath { get; set; }

    private BatchItemStatus _status;

    /// <summary>当前处理状态</summary>
    public BatchItemStatus Status
    {
        get => _status;
        set
        {
            if (_status != value)
            {
                _status = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status)));
            }
        }
    }

    /// <summary>失败时的错误信息</summary>
    public string? ErrorMessage { get; set; }

    private double _progress;

    /// <summary>进度百分比 (0–100)</summary>
    public double Progress
    {
        get => _progress;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);
            if (Math.Abs(_progress - clamped) > 0.01)
            {
                _progress = clamped;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Progress)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
