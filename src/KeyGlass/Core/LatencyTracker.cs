namespace KeyGlass.Core;

/// <summary>钩子回调时间 → 首帧渲染提交时间 的延迟统计（毫秒）。</summary>
public sealed class LatencyTracker
{
    private readonly double[] _samples = new double[1024];
    private int _count;
    private int _head;
    private double _sum;
    private int _sinceSort = 512;      // 触发一次排序
    private double _p50;
    private double _p95;
    private double _max;

    public int Count => _count;

    public void Add(double ms)
    {
        if (double.IsNaN(ms) || ms < 0 || ms > 5000) return;

        if (_count < _samples.Length)
        {
            _samples[_count] = ms;
            _sum += ms;
            _count++;
        }
        else
        {
            _sum -= _samples[_head];
            _sum += ms;
            _samples[_head] = ms;
            _head = (_head + 1) % _samples.Length;
        }

        if (ms > _max) _max = ms;

        if (++_sinceSort >= 16)
        {
            Recompute();
        }
    }

    private void Recompute()
    {
        _sinceSort = 0;
        if (_count == 0) return;

        var copy = new double[_count];
        Array.Copy(_samples, copy, _count);
        Array.Sort(copy);
        _p50 = copy[Math.Min(_count - 1, (int)(_count * 0.50))];
        _p95 = copy[Math.Min(_count - 1, (int)(_count * 0.95))];
        _max = copy[_count - 1];
    }

    public void Snapshot(out double p50, out double p95, out double max, out double avg)
    {
        if (_count == 0)
        {
            p50 = p95 = max = avg = 0;
            return;
        }

        Recompute();
        p50 = _p50;
        p95 = _p95;
        max = _max;
        avg = _sum / _count;
    }

    public void Reset()
    {
        _count = 0;
        _head = 0;
        _sum = 0;
        _p50 = _p95 = _max = 0;
        _sinceSort = 512;
    }
}
