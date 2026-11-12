using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Atoll.Services;

public sealed record ActiveAudioSessionInfo(int ProcessId, string ProcessName, string DisplayName, bool IsMuted, AudioSessionState State);

public sealed class AudioEndpointService : IMMNotificationClient, IDisposable
{
    private readonly object _sync = new();
    private MMDeviceEnumerator? _enumerator;
    private MMDevice? _renderDevice;
    private WasapiLoopbackCapture? _loopback;
    private bool _visualizerRequested;
    private bool _disposed;
    private readonly float[] _bands = new float[5];
    private readonly float[][] _bandBuffers = [new float[5], new float[5]];
    private int _bandBufferIndex;
    private long _lastSpectrumDispatchTicks;

    public float VolumePercent { get; private set; }
    public bool IsMuted { get; private set; }
    public bool IsAvailable { get; private set; }

    public event EventHandler<(float VolumePercent, bool IsMuted)>? VolumeChanged;
    public event EventHandler<float[]>? SpectrumUpdated;
    public event EventHandler? AudioSessionsMightHaveChanged;

    public AudioEndpointService()
    {
        InitializeDevice();
    }

    private void InitializeDevice()
    {
        lock (_sync)
        {
            try
            {
                _enumerator ??= new MMDeviceEnumerator();
                try
                {
                    _enumerator.RegisterEndpointNotificationCallback(this);
                }
                catch
                {
                    // Already registered or unsupported
                }

                AttachDefaultRenderDevice();
            }
            catch (Exception ex)
            {
                IsAvailable = false;
                LoggingService.Instance.Warn("Audio", "Failed to initialize CoreAudio endpoint.", ex);
            }
        }
    }

    private void AttachDefaultRenderDevice()
    {
        DetachRenderDevice();
        if (_enumerator is null) return;

        try
        {
            _renderDevice = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            _renderDevice.AudioEndpointVolume.OnVolumeNotification += OnEndpointVolumeNotification;
            VolumePercent = _renderDevice.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;
            IsMuted = _renderDevice.AudioEndpointVolume.Mute;
            IsAvailable = true;

            if (_visualizerRequested)
            {
                StartLoopbackInternal();
            }
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            LoggingService.Instance.Debug("Audio", $"No default render endpoint: {ex.Message}");
        }
    }

    private void DetachRenderDevice()
    {
        StopLoopbackInternal();
        if (_renderDevice is not null)
        {
            try
            {
                _renderDevice.AudioEndpointVolume.OnVolumeNotification -= OnEndpointVolumeNotification;
                _renderDevice.Dispose();
            }
            catch
            {
                // Best-effort cleanup
            }
            _renderDevice = null;
        }
    }

    private void OnEndpointVolumeNotification(AudioVolumeNotificationData data)
    {
        VolumePercent = data.MasterVolume * 100f;
        IsMuted = data.Muted;
        VolumeChanged?.Invoke(this, (VolumePercent, IsMuted));
    }

    public void SetVolumePercent(double percent)
    {
        lock (_sync)
        {
            if (_renderDevice is null) return;
            try
            {
                float scalar = (float)Math.Clamp(percent / 100.0, 0.0, 1.0);
                _renderDevice.AudioEndpointVolume.MasterVolumeLevelScalar = scalar;
                if (scalar > 0.001f && _renderDevice.AudioEndpointVolume.Mute)
                {
                    _renderDevice.AudioEndpointVolume.Mute = false;
                }
            }
            catch (Exception ex)
            {
                LoggingService.Instance.Debug("Audio", $"SetVolume failed: {ex.Message}");
            }
        }
    }

    public void AdjustVolume(double deltaPercent)
    {
        SetVolumePercent(VolumePercent + deltaPercent);
    }

    public void ToggleMute()
    {
        lock (_sync)
        {
            if (_renderDevice is null) return;
            try
            {
                _renderDevice.AudioEndpointVolume.Mute = !_renderDevice.AudioEndpointVolume.Mute;
            }
            catch
            {
                // Ignore
            }
        }
    }

    public void SetVisualizerActive(bool active)
    {
        lock (_sync)
        {
            if (_disposed) return;
            if (_visualizerRequested == active) return;
            _visualizerRequested = active;

            if (active)
            {
                StartLoopbackInternal();
            }
            else
            {
                StopLoopbackInternal();
                Array.Clear(_bands);
                int idx = _bandBufferIndex ^ 1;
                _bandBufferIndex = idx;
                Array.Clear(_bandBuffers[idx]);
                SpectrumUpdated?.Invoke(this, _bandBuffers[idx]);
            }
        }
    }

    public IReadOnlyList<ActiveAudioSessionInfo> GetActiveCommunicationSessions()
    {
        var results = new List<ActiveAudioSessionInfo>();
        lock (_sync)
        {
            if (_enumerator is null) return results;
            try
            {
                using var comms = _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                var mgr = comms.AudioSessionManager;
                var sessions = mgr.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    using var session = sessions[i];
                    if (session.State != AudioSessionState.AudioSessionStateActive) continue;
                    int pid = (int)session.GetProcessID;
                    string procName = string.Empty;
                    try
                    {
                        using var proc = Process.GetProcessById(pid);
                        procName = proc.ProcessName;
                    }
                    catch
                    {
                        continue;
                    }

                    results.Add(new ActiveAudioSessionInfo(
                        pid,
                        procName,
                        session.DisplayName ?? procName,
                        session.SimpleAudioVolume.Mute,
                        session.State));
                }
            }
            catch
            {
                // No capture endpoint or access issue
            }
        }
        return results;
    }

    public bool TryToggleSessionMute(int processId)
    {
        lock (_sync)
        {
            if (_enumerator is null) return false;
            bool toggled = false;
            foreach (DataFlow flow in new[] { DataFlow.Capture, DataFlow.Render })
            {
                try
                {
                    using var dev = _enumerator.GetDefaultAudioEndpoint(flow, Role.Communications);
                    var sessions = dev.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        using var s = sessions[i];
                        if ((int)s.GetProcessID == processId)
                        {
                            s.SimpleAudioVolume.Mute = !s.SimpleAudioVolume.Mute;
                            toggled = true;
                        }
                    }
                }
                catch
                {
                    // Ignore missing endpoint
                }
            }
            return toggled;
        }
    }

    private void StartLoopbackInternal()
    {
        if (_loopback is not null || _renderDevice is null) return;
        try
        {
            _loopback = new WasapiLoopbackCapture(_renderDevice);
            _loopback.DataAvailable += OnLoopbackDataAvailable;
            _loopback.StartRecording();
        }
        catch (Exception ex)
        {
            LoggingService.Instance.Debug("Audio", $"Loopback visualizer unavailable: {ex.Message}");
            StopLoopbackInternal();
        }
    }

    private void StopLoopbackInternal()
    {
        if (_loopback is null) return;
        try
        {
            _loopback.DataAvailable -= OnLoopbackDataAvailable;
            _loopback.StopRecording();
            _loopback.Dispose();
        }
        catch
        {
            // Ignore shutdown errors
        }
        finally
        {
            _loopback = null;
        }
    }

    private void OnLoopbackDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_disposed || e.BytesRecorded < 32 || _loopback is null) return;

        // Throttle UI updates to ~25Hz to keep CPU minimal
        long now = Stopwatch.GetTimestamp();
        if ((now - _lastSpectrumDispatchTicks) < Stopwatch.Frequency / 25) return;
        _lastSpectrumDispatchTicks = now;

        int bytesPerSample = _loopback.WaveFormat.BitsPerSample / 8;
        if (bytesPerSample != 4) return;

        int sampleCount = e.BytesRecorded / 4;
        int stride = Math.Max(1, sampleCount / 160);
        int bandWidth = Math.Max(1, sampleCount / 5);

        int idx = _bandBufferIndex ^ 1;
        _bandBufferIndex = idx;
        float[] publish = _bandBuffers[idx];

        for (int b = 0; b < 5; b++)
        {
            int start = b * bandWidth;
            int end = Math.Min(sampleCount, start + bandWidth);
            double sumSq = 0;
            int count = 0;

            for (int i = start; i < end; i += stride)
            {
                float sample = BitConverter.ToSingle(e.Buffer, i * 4);
                sumSq += sample * sample;
                count++;
            }

            float rms = count > 0 ? (float)Math.Sqrt(sumSq / count) : 0f;
            float normalized = Math.Clamp(rms * 4.2f, 0.08f, 1.0f);
            float smoothed = _bands[b] * 0.45f + normalized * 0.55f;
            _bands[b] = smoothed;
            publish[b] = smoothed;
        }

        SpectrumUpdated?.Invoke(this, publish);
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState)
    {
        AudioSessionsMightHaveChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnDeviceAdded(string pwstrDeviceId) { }
    public void OnDeviceRemoved(string deviceId) { }

    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia)
        {
            lock (_sync)
            {
                if (!_disposed) AttachDefaultRenderDevice();
            }
        }
        AudioSessionsMightHaveChanged?.Invoke(this, EventArgs.Empty);
    }

    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            DetachRenderDevice();
            if (_enumerator is not null)
            {
                try
                {
                    _enumerator.UnregisterEndpointNotificationCallback(this);
                    _enumerator.Dispose();
                }
                catch
                {
                    // Ignore
                }
                _enumerator = null;
            }
        }
    }
}

