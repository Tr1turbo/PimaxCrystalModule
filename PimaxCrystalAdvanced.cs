﻿using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VRCFaceTracking;
using VRCFaceTracking.Core.Types;

namespace PimaxCrystalAdvanced;

public class PimaxCrystalAdvanced : ExtTrackingModule
{
    private BrokenEye.Client? _beClient;
    private Tobii.Client? _tobiiClient;

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, false);

    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable,
        bool expressionAvailable)
    {
        Logger.LogInformation("Initializing module...");

        Logger.LogInformation("Try use BrokenEye API...");
        _beClient = new BrokenEye.Client(Logger);
        if (_beClient.Connect("127.0.0.1", 5555))
        {
            Logger.LogInformation("Connected to Broken Eye server!");

            _beClient.OnData += UpdateEyeData;


            var stream = typeof(PimaxCrystalAdvanced).Assembly.GetManifestResourceStream("PimaxCrystalAdvanced.Assets.crystal-hmd.png");
            ModuleInformation.StaticImages = stream is not null ? new List<Stream> { stream } : ModuleInformation.StaticImages;
            ModuleInformation.Name = "Pimax Crystal";

            return (true, false);
        }




        Logger.LogInformation("Failed to connect to Broken Eye server...");

        Logger.LogInformation("Try use Tobii API...");
        _tobiiClient = new Tobii.Client(Logger);
        if (_tobiiClient.Connect())
        {
            Logger.LogInformation("Connected to Tobii API!");

            _tobiiClient.OnData += UpdateEyeData;


            var stream = typeof(PimaxCrystalAdvanced).Assembly.GetManifestResourceStream("PimaxCrystalAdvanced.Assets.crystal-tobii.png");
            ModuleInformation.StaticImages = stream is not null ? new List<Stream> { stream } : ModuleInformation.StaticImages;
            ModuleInformation.Name = "Pimax Crystal";

            return (true, false);
        }

        Logger.LogInformation("Failed to connect to Tobii...");

        return (false, false);
    }

    private readonly Channel<EyeData> _eyeDataChannel = Channel.CreateUnbounded<EyeData>();

    private void UpdateEyeData(EyeData data)
    {
        _eyeDataChannel.Writer.TryWrite(data);
    }

    private double _minValidPupilDiameterMm = 999f;

    public override void Update()
    {
        var task = _eyeDataChannel.Reader.ReadAsync().AsTask();
        // We block the loop and wait for data, since a wasted spinning loop eats up a lot of CPU.
        task.Wait();

        var data = task.Result;

        UnifiedTracking.Data.Eye.Left.Gaze =
            data.Left.GazeDirectionIsValid ? ToVrcftVector2(data.Left.GazeDirection) : Vector2.zero;
        UnifiedTracking.Data.Eye.Right.Gaze =
            data.Right.GazeDirectionIsValid ? ToVrcftVector2(data.Right.GazeDirection) : Vector2.zero;

        UnifiedTracking.Data.Eye.Left.Openness = data.Left.OpennessIsValid ? MapValue(data.Left.Openness, 0, 0.8f, 0, 1.33f) : 1f;
        UnifiedTracking.Data.Eye.Right.Openness = data.Right.OpennessIsValid ? MapValue(data.Right.Openness, 0, 0.8f, 0, 1.33f) : 1f;
     
        if (data.Left.PupilDiameterIsValid)
            UnifiedTracking.Data.Eye.Left.PupilDiameter_MM = data.Left.PupilDiameterMm;

        if (data.Right.PupilDiameterIsValid)
            UnifiedTracking.Data.Eye.Right.PupilDiameter_MM = data.Right.PupilDiameterMm;

        // Overwrite the minimum pupil diameter, since if the headset is removed, VRCFT will set it to 0
        // it will no longer be updated, even if the headset is put on again.
        // So I'll overwrite the min diameter it is used independently every update, if it is valid and greater
        // than the minimum threshold.
        const float minPupilDiameterThreshold = 1f;
        if (data.Left.PupilDiameterIsValid
            && data.Right.PupilDiameterIsValid
            && data.Left.PupilDiameterMm > minPupilDiameterThreshold
            && data.Right.PupilDiameterMm > minPupilDiameterThreshold
           )
        {
            _minValidPupilDiameterMm = Math.Min(_minValidPupilDiameterMm,
                (data.Left.PupilDiameterMm + data.Right.PupilDiameterMm) / 2.0);
        }

        if (data.Left.PupilDiameterIsValid || data.Right.PupilDiameterIsValid)
        {
            UnifiedTracking.Data.Eye._minDilation = (float)_minValidPupilDiameterMm;
        }
    }

    private static Vector2 ToVrcftVector2(EyeData.Vector2 v)
    {
        return new Vector2(v.X, v.Y);
    }

    public override void Teardown()
    {
        _beClient?.Dispose();
        _tobiiClient?.Dispose();
    }

    static float MapValue(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        // Ensure the value is within the specified range
        value = Math.Min(Math.Max(value, fromMin), fromMax);

        // Map the value from one range to another
        float mappedValue = (value - fromMin) * (toMax - toMin) / (fromMax - fromMin) + toMin;

        return mappedValue;
    }
}
