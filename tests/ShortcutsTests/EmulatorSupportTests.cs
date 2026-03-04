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
        var api = CreatePlayniteApi();
        var logger = Substitute.For<ILogger>();
        var game = CreateGame("Test Game");
        var action = CreateEmulatorAction(Guid.NewGuid(), "profile-1");

        var database = Substitute.For<IGameDatabaseAPI>();
        var emulators = CreateEmulatorCollection();
        database.Emulators.Returns(emulators);
        api.Database.Returns(database);

        var result = ImportExportService.BuildEmulatorActionResult(api, logger, game, action);

        Assert.Null(result.action);
        Assert.Equal("Test Game", result.name);
    }

    [Fact]
    public void BuildCustomEmulatorResult_OverrideDefaultArgs_UsesOnlyActionArgs()
    {
        var api = CreatePlayniteApi();
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

        var result = ImportExportService.BuildCustomEmulatorResult(api, logger, game, action, profile, @"C:\Emu", "Test Game");

        Assert.NotNull(result.action);
        Assert.Equal("--fullscreen --volume=50", result.action.Arguments);
    }

    [Fact]
    public void BuildCustomEmulatorResult_WithoutOverride_CombinesArgs()
    {
        var api = CreatePlayniteApi();
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

        var result = ImportExportService.BuildCustomEmulatorResult(api, logger, game, action, profile, @"C:\Emu", "Test Game");

        Assert.NotNull(result.action);
        Assert.Equal("\"{ImagePath}\" --fullscreen", result.action.Arguments);
    }

    [Fact]
    public void BuildCustomEmulatorResult_EmptyExecutable_ReturnsNullAction()
    {
        var api = CreatePlayniteApi();
        var logger = Substitute.For<ILogger>();
        var game = CreateGame("Test Game");
        var action = CreateEmulatorAction(Guid.NewGuid(), "profile-1");

        var profile = new CustomEmulatorProfile
        {
            Executable = "",
            Arguments = "-f"
        };

        var result = ImportExportService.BuildCustomEmulatorResult(api, logger, game, action, profile, @"C:\Emu", "Test Game");

        Assert.Null(result.action);
    }

    [Fact]
    public void BuildBuiltInEmulatorResult_ProfileOverride_DefaultArgs_UsesCustomArgsOnly()
    {
        var api = CreatePlayniteApi();
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
            api,
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
        var api = CreatePlayniteApi();
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
            api,
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

    private static IPlayniteAPI CreatePlayniteApi()
    {
        var api = Substitute.For<IPlayniteAPI>();
        api.Database.Returns(Substitute.For<IGameDatabaseAPI>());
        api.Emulation.Returns(Substitute.For<IEmulationAPI>());
        api.ExpandGameVariables(Arg.Any<Game>(), Arg.Any<string>(), Arg.Any<string>())
            .Returns(info => info.ArgAt<string>(1));
        return api;
    }

    private static IItemCollection<Emulator> CreateEmulatorCollection(params Emulator[] emulators)
    {
        var collection = Substitute.For<IItemCollection<Emulator>>();
        var list = new List<Emulator>(emulators);

        collection.GetEnumerator().Returns(list.GetEnumerator());
        ((System.Collections.IEnumerable)collection).GetEnumerator().Returns(list.GetEnumerator());
        collection.ContainsItem(Arg.Any<Guid>()).Returns(callInfo =>
        {
            var id = callInfo.Arg<Guid>();
            return list.Exists(emulator => emulator.Id == id);
        });

        return collection;
    }
}
