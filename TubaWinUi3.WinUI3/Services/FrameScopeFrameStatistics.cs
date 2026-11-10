// SPDX-License-Identifier: GPL-3.0-only
// Adapted from Misakamiro/FrameScopeMonitor, statistics v2.
// Source blob b242dafb3bb5b78848cef58a325c75bba2084c22 (2026-11-05).
using System;
using System.Collections.Generic;

// Independent statistics-v2 implementation. Source frames are never edited.
namespace TubaWinUi3.Services;

internal static class FrameScopeFrameStatistics
{
    internal static double TrackScore(int hardwareRows, int rows, List<double> sorted) {
        double median = sorted[(int)Math.Ceiling(sorted.Count * .5) - 1];
        double p99 = sorted[(int)Math.Ceiling(sorted.Count * .99) - 1];
        return hardwareRows * 3.0 + rows + Math.Min(240, 1000.0 / median) * 20 - Math.Min(1000, p99) * 2;
    }
    public const int Version = 2;
    public const double WindowMs = 1000;
    public const double UpdateMs = 100;
    public const double SampleMs = 1000;
    public const double MaxTimeMs = 604800000;

    public struct Frame {
        public double TimeMs, Ms;
        public Frame(double timeMs, double ms) { TimeMs=timeMs; Ms=ms; }
    }
    public struct Sample {
        public double TimeMs;
        public int Count;
        public double? Average, Low1, Low01;
    }
    public sealed class Values { public double? Average, Low1, Low01; }
    public sealed class Result {
        public List<Sample> Samples = new();
        public readonly List<bool> Included=new List<bool>();
        public Values Raw = new(), Filtered = new();
        public double? MinimumFps, MaximumFps;
        public int FrameCount, ValidSamples, RetainedSamples, ExcludedSamples, MissingSamples;
    }
    public static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    public static double Rounded(double value) {
        if(!Finite(value))throw new ArgumentException("Non-finite frame statistic");
        return Math.Floor(value+.5);
    }
    public static double? LowFromSorted(IList<double> sorted, double fraction) {
        if(sorted.Count==0)return null;
        int count=Math.Max(1,(int)Math.Floor(sorted.Count*fraction));
        double sum=0;
        for(int i=sorted.Count-1,taken=0;taken<count;i--,taken++)sum+=sorted[i];
        return Rounded(1000.0/(sum/count));
    }
    public static Sample Snapshot(IList<double> values) {
        var result=new Sample { Count=values.Count };
        if(values.Count==0)return result;
        var sorted=new List<double>(values.Count);double sum=0;
        foreach(double value in values) {
            if(!Finite(value)||value<=0)throw new ArgumentException("Invalid frame time");
            sum+=value;sorted.Add(value);
        }
        sorted.Sort();
        result.Average=Rounded(1000.0*values.Count/sum);
        result.Low1=LowFromSorted(sorted,.01);result.Low01=LowFromSorted(sorted,.001);
        return result;
    }
    public static double? MeanInteger(IList<double> values) {
        if(values.Count==0)return null;
        double sum=0;foreach(double value in values)sum+=value;
        return Rounded(Math.Truncate(sum/values.Count*100.0)/100.0);
    }
    private static double Segment(List<double> sorted,double start,double end) {
        int a=(int)Rounded(sorted.Count*start),b=(int)Rounded(sorted.Count*end);
        if(a<0)a=Math.Max(0,sorted.Count+a);else a=Math.Min(a,sorted.Count);
        if(b<0)b=Math.Max(0,sorted.Count+b);else b=Math.Min(b,sorted.Count);
        return MeanInteger(sorted.GetRange(a,Math.Max(0,b-a)))??0;
    }
    private static double?[] Bounds(List<double> values) {
        if(values.Count==0)return new double?[]{null,null};
        var sorted=new List<double>(values);sorted.Sort();
        double middle=Segment(sorted,.325,.775),lower=0,upper=0,start=.005,end=.105;
        bool found=false;
        for(int i=0;i<=200;i++) {
            lower=Segment(sorted,start,end);
            if(middle==0||lower/middle>=.35){found=true;break;}
            start+=.005;end+=.005;
        }
        if(!found)throw new InvalidOperationException("Lower frame filter did not converge");
        start=.895;end=.995;found=false;
        for(int i=0;i<=200;i++) {
            upper=Segment(sorted,start,end);
            if((middle==0&&upper==0)||(middle!=0&&upper/middle<=1.35)){found=true;break;}
            start-=.005;end-=.005;
        }
        if(!found)throw new InvalidOperationException("Upper frame filter did not converge");
        return new double?[]{Rounded(lower*.45),Rounded(upper*1.05)};
    }
    private static Values Metrics(List<Sample> samples,List<bool> included,bool filtered) {
        var average=new List<double>();var low1=new List<double>();var low01=new List<double>();
        for(int i=0;i<samples.Count;i++) {
            if(filtered&&!included[i])continue;
            Sample sample=samples[i];
            if(sample.Average.HasValue)average.Add(sample.Average.Value);
            if(sample.Low1.HasValue)low1.Add(sample.Low1.Value);
            if(sample.Low01.HasValue)low01.Add(sample.Low01.Value);
        }
        return new Values { Average=MeanInteger(average),Low1=MeanInteger(low1),Low01=MeanInteger(low01) };
    }
    public static Result Summarize(List<Sample> samples) {
        var result=new Result { Samples=samples };var fps=new List<double>();
        foreach(Sample sample in samples)if(sample.Average.HasValue)fps.Add(sample.Average.Value);
        var bounds=Bounds(fps);result.MinimumFps=bounds[0];result.MaximumFps=bounds[1];
        foreach(Sample sample in samples) {
            bool keep=sample.Average.HasValue&&sample.Average>=bounds[0]&&sample.Average<=bounds[1];
            result.Included.Add(keep);if(keep)result.RetainedSamples++;
        }
        result.ValidSamples=fps.Count;result.MissingSamples=samples.Count-fps.Count;
        result.ExcludedSamples=fps.Count-result.RetainedSamples;
        result.Raw=Metrics(samples,result.Included,false);result.Filtered=Metrics(samples,result.Included,true);
        return result;
    }
    private static Sample SampleAt(IList<Frame> frames,double origin,double boundary,ref int left,ref int right) {
        double absolute=origin+boundary;
        while(right<frames.Count&&frames[right].TimeMs<=absolute)right++;
        while(left<right&&frames[left].TimeMs<=absolute-WindowMs)left++;
        var values=new List<double>(right-left);
        for(int i=left;i<right;i++)values.Add(frames[i].Ms);
        Sample result=Snapshot(values);result.TimeMs=boundary;return result;
    }
    public static Result Replay(IList<Frame> frames) {
        var samples=new List<Sample>();
        if(frames.Count==0)return Summarize(samples);
        double previous=-1;
        foreach(Frame frame in frames) {
            if(!Finite(frame.TimeMs)||!Finite(frame.Ms)||frame.TimeMs<previous||frame.TimeMs<0||frame.TimeMs>MaxTimeMs||frame.Ms<=0||frame.Ms>MaxTimeMs)
                throw new ArgumentException("Invalid selected frame stream");
            previous=frame.TimeMs;
        }
        double origin=frames[0].TimeMs,end=frames[frames.Count-1].TimeMs-origin;int left=0,right=0;
        for(double boundary=SampleMs;boundary<=end;boundary+=SampleMs)samples.Add(SampleAt(frames,origin,boundary,ref left,ref right));
        if(samples.Count==0||samples[samples.Count-1].TimeMs!=end)samples.Add(SampleAt(frames,origin,end,ref left,ref right));
        Result result=Summarize(samples);result.FrameCount=frames.Count;return result;
    }
}
