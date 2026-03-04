using NSubstitute;
using Playnite.SDK;
using Playnite.SDK.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Xunit;

namespace SteamShortcutsImporter.Tests;

public class EmulatorSupportTests
{
    [Fact]
    public void BuildEmulatorActionResult_MissingEmulator_ReturnsNullAction()
    {
        var expand = CreateExpand();
        var logger = Substitute.For<ILogger>();
        var game = CreateGame("Test Game");
        var action = CreateEmulatorAction(Guid.NewGuid(), "profile-1");

        var emulators = new List<Emulator>();
        var emulation = Substitute.For<IEmulationAPI>();

        var result = ImportExportService.BuildEmulatorActionResult(emulators, emulation, expand, logger, game, action);

        Assert.Null(result.action);
        Assert.Equal("Test Game", result.name);
    }

    [Fact]
    public void BuildCustomEmulatorResult_OverrideDefaultArgs_UsesOnlyActionArgs()
    {
        var expand = CreateExpand();
        var logger = Substitute.For<ILogger>();
        var game = CreateGame("Test Game");
        var action = CreateEmulatorAction(Guid.NewGuid(), "profile-1");
        action.OverrideDefaultArgs = true;
        action.AdditionalArguments = "--fullscreen --volume=50";

        var profile = new CustomEmulatorProfile
        {
            Executable = @"C:\Emu\emu.exe",
            Arguments = "--default-arg",
            WorkingDirectory = @"C:\Emu"
        };

        var result = ImportExportService.BuildCustomEmulatorResult(expand, logger, game, action, profile, @"C:\Emu", "Test Game");

        Assert.NotNull(result.action);
        Assert.Equal("--fullscreen --volume=50", result.action.Arguments);
    }

    [Fact]
    public void BuildCustomEmulatorResult_WithoutOverride_CombinesArgs()
    {
        var expand = CreateExpand();
        var logger = Substitute.For<ILogger>();
        var game = CreateGame("Test Game");
        var action = CreateEmulatorAction(Guid.NewGuid(), "profile-1");
        action.AdditionalArguments = "--fullscreen";

        var profile = new CustomEmulatorProfile
        {
            Executable = @"C:\Emu\emu.exe",
            Arguments = "\"{ImagePath}\"",
            WorkingDirectory = @"C:\Emu"
        };

        var result = ImportExportService.BuildCustomEmulatorResult(expand, logger, game, action, profile, @"C:\Emu", "Test Game");

        Assert.NotNull(result.action);
        Assert.Equal("\"{ImagePath}\" --fullscreen", result.action.Arguments);
    }

    [Fact]
    public void BuildCustomEmulatorResult_EmptyExecutable_ReturnsNullAction()
    {
        var expand = CreateExpand();
        var logger = Substitute.For<ILogger>();
        var game = CreateGame("Test Game");
        var action = CreateEmulatorAction(Guid.NewGuid(), "profile-1");

        var profile = new CustomEmulatorProfile
        {
            Executable = "",
            Arguments = "-f"
        };

        var result = ImportExportService.BuildCustomEmulatorResult(expand, logger, game, action, profile, @"C:\Emu", "Test Game");

        Assert.Null(result.action);
    }

    [Fact]
    public void BuildBuiltInEmulatorResult_ProfileOverride_DefaultArgs_UsesCustomArgsOnly()
    {
        var expand = CreateExpand();
        var logger = Substitute.For<ILogger>();
        var emulation = Substitute.For<IEmulationAPI>();
        var game = CreateGame("Test Game");
        var action = CreateEmulatorAction(Guid.NewGuid(), "profile-1");
        action.AdditionalArguments = "--user-arg";

        var profile = new BuiltInEmulatorProfile
        {
            BuiltInProfileName = "Balanced",
            CustomArguments = "--custom-arg",
            OverrideDefaultArgs = true
        };

        var definition = new EmulatorDefinition
        {
            Id = "retroarch",
            Profiles = new List<EmulatorDefinitionProfile>
            {
                new EmulatorDefinitionProfile
                {
                    Name = "Balanced",
                    StartupExecutable = @"retroarch.exe",
                    StartupArguments = @"-L ""{ImagePath}"""
                }
            }
        };

        emulation.GetEmulator("retroarch").Returns(definition);

        var result = ImportExportService.BuildBuiltInEmulatorResult(
            expand,
            logger,
            emulation,
            game,
            action,
            profile,
            @"C:\RetroArch",
            "retroarch",
            "Test Game");

        Assert.NotNull(result.action);
        Assert.Equal("--custom-arg", result.action.Arguments);
    }

    [Fact]
    public void BuildBuiltInEmulatorResult_ActionOverride_DefaultArgs_UsesOnlyActionArgs()
    {
        var expand = CreateExpand();
        var logger = Substitute.For<ILogger>();
        var emulation = Substitute.For<IEmulationAPI>();
        var game = CreateGame("Test Game");
        var action = CreateEmulatorAction(Guid.NewGuid(), "profile-1");
        action.OverrideDefaultArgs = true;
        action.AdditionalArguments = "--user-arg";

        var profile = new BuiltInEmulatorProfile
        {
            BuiltInProfileName = "Balanced",
            CustomArguments = "--custom-arg",
            OverrideDefaultArgs = false
        };

        var definition = new EmulatorDefinition
        {
            Id = "retroarch",
            Profiles = new List<EmulatorDefinitionProfile>
            {
                new EmulatorDefinitionProfile
                {
                    Name = "Balanced",
                    StartupExecutable = @"retroarch.exe",
                    StartupArguments = @"-L ""{ImagePath}"""
                }
            }
        };

        emulation.GetEmulator("retroarch").Returns(definition);

        var result = ImportExportService.BuildBuiltInEmulatorResult(
            expand,
            logger,
            emulation,
            game,
            action,
            profile,
            @"C:\RetroArch",
            "retroarch",
            "Test Game");

        Assert.NotNull(result.action);
        Assert.Equal("--user-arg", result.action.Arguments);
    }

    private static Game CreateGame(string name)
    {
        return new Game
        {
            Id = Guid.NewGuid(),
            Name = name,
            GameActions = new ObservableCollection<GameAction>()
        };
    }

    private static GameAction CreateEmulatorAction(Guid emulatorId, string profileId)
    {
        return new GameAction
        {
            Type = GameActionType.Emulator,
            EmulatorId = emulatorId,
            EmulatorProfileId = profileId,
            IsPlayAction = true
        };
    }

    private static Func<Game, string, string, string> CreateExpand()
    {
        return (_, input, __) => input;
    }
}
