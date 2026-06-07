using System.IO;
using Newtonsoft.Json;
using Shared.Configs;
using System;
using Xunit;

namespace ServerTests.Shared.Configs;

public class SVarsTests
{
    private string _configPath = "test-config.json";

    [Fact]
    public void LoadConfig_WithValidPath_ReturnsCorrectData()
    {
        SVars.LoadFromJson(_configPath);
        var config = SVars.Instance;

        Assert.Equal("192.168.0.101", config.Ip);
        Assert.Equal(8921, config.Port);
        Assert.Equal(70, config.MaxPlayers);
        Assert.Equal(40, config.TickRate);
        Assert.Equal("VGVzdF9zZXJ2ZXIx", config.ConnectionKey);
    }

    [Fact]
    public void LoadConfig_WithInvalidPath_ReturnsDefaultValues()
    {
        SVars.LoadFromJson("test_incorrect_config.json");
        var config = SVars.Instance;

        Assert.Equal("0.0.0.0", config.Ip);
        Assert.Equal(7777, config.Port);
        Assert.Equal(100, config.MaxPlayers);
        Assert.Equal(30, config.TickRate);
        Assert.Equal("", config.ConnectionKey);
    }

    [Fact]
    public void LoadConfig_WithEmptyFile_UsesDefaultValues()
    {
        var empty_configPath = "empty-config.json";
        System.IO.File.WriteAllText(empty_configPath, "{}");

        SVars.LoadFromJson(empty_configPath);
        var config = SVars.Instance;

        Assert.Equal("0.0.0.0", config.Ip);
        Assert.Equal(7777, config.Port);
        Assert.Equal(100, config.MaxPlayers);
        Assert.Equal(30, config.TickRate);
        Assert.Equal("", config.ConnectionKey);

        System.IO.File.Delete(empty_configPath);
    }
}