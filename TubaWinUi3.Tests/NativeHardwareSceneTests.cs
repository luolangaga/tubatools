using System.Numerics;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class NativeHardwareSceneTests
{
    [Theory]
    [InlineData(-1)] [InlineData(float.NaN)] [InlineData(float.PositiveInfinity)]
    public void UnavailableTemperatureIsNeverReportedAsCold(float temperature) => Assert.Equal(-1,HardwareSceneModel.Heat(temperature));

    [Fact]
    public void HeatAndFanPolicyAreBoundedAndIncreaseWithMeasuredInput()
    {
        Assert.Equal(0,HardwareSceneModel.Heat(30)); Assert.Equal(1,HardwareSceneModel.Heat(110));
        Assert.True(HardwareSceneModel.Heat(70)>HardwareSceneModel.Heat(50));
        Assert.True(HardwareSceneModel.FanSpeed(100)>HardwareSceneModel.FanSpeed(20));
        Assert.Equal(HardwareSceneModel.FanSpeed(100),HardwareSceneModel.FanSpeed(1000));
        Assert.Equal(HardwareSceneModel.FanSpeed(-1),HardwareSceneModel.FanSpeed(float.NaN));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void ComputerModelsHaveFiniteGeometryAndAllHardwareAnchors(bool laptop)
    {
        var model=HardwareSceneModel.Computer(laptop);
        foreach(var name in new[]{"处理器","显卡","内存","硬盘","主板","BIOS","显示器","系统","NPU","网卡","声卡","键盘","摄像头"}) Assert.True(model.Anchors.ContainsKey(name),name);
        Assert.NotEmpty(model.Faces);
        Assert.All(model.Faces,face=>Assert.All(face.Points,p=>Assert.True(float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z))));
        if(!laptop) Assert.Contains(model.Faces,face=>face.Rotor is not null);
    }

    [Fact]
    public void KeyboardPreservesPhysicalKeyIdentityAndSize()
    {
        var model=new HardwareSceneModel();
        model.Keyboard([new(0xA0,"LShift",0,0,2,1),new(0xA1,"RShift",2.1f,0,2,1),new(0xE01C,"Enter",4.2f,0,1,2)],Vector3.Zero);
        Assert.Contains(model.Faces,f=>f.Key==0xA0); Assert.Contains(model.Faces,f=>f.Key==0xA1);
        var enter=model.Faces.Where(f=>f.Key==0xE01C).SelectMany(f=>f.Points).ToArray();
        Assert.True(enter.Max(p=>p.Z)-enter.Min(p=>p.Z)>enter.Max(p=>p.X)-enter.Min(p=>p.X));
    }

    [Fact]
    public void AdvancingInspectionDoesNotMarkUnconfirmedStepsPassed()
    {
        var session=new DeviceInspectionSession(); session.ModelStates(7);
        Assert.All(Enumerable.Range(0,8),i=>Assert.Equal(DeviceInspectionSession.Result.Pending,session[i]));
        Assert.Contains("未检查 8 项",session.Report);
    }

    [Fact]
    public void LaterStressConfirmationCannotHideAConfigurationProblem()
    {
        var session=new DeviceInspectionSession(); session[1]=DeviceInspectionSession.Result.Problem;
        session[7]=DeviceInspectionSession.Result.Confirmed;
        Assert.Equal("发现问题",session.ModelStates(7)["处理器"]);
        session[1]=DeviceInspectionSession.Result.Confirmed;
        Assert.Equal("已确认",session.ModelStates(-1)["处理器"]);
    }

    [Theory]
    [InlineData("处理器","intel")] [InlineData("显卡",null)] [InlineData("内存",null)] [InlineData("硬盘",null)] [InlineData("主板",null)]
    public async Task DistributedNativeMeshesDecodeWithoutBrowser(string part,string? brand)
    {
        var model=await NativeHardwareModelLoader.PrepareAsync(part,brand);
        Assert.NotNull(model); Assert.NotEmpty(model.Faces);
        Assert.All(model.Faces,f=>Assert.True(f.Texture<model.Textures.Count));
        if(part!="内存") Assert.NotEmpty(model.Textures);
    }
}
