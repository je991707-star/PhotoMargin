using PhotoMargin.App.ViewModels;

namespace PhotoMargin.App.LogicTests;

public sealed partial class StateAndPersistenceTests
{
    [Fact]
    public void LateOldPageUnloadCannotStopNewPagePreview() => Ui.Run(async () =>
    {
        using var vm = Create(); await vm.InitializeAsync();
        var oldPage = new object(); var newPage = new object();
        vm.RuleEditor.StartPreview(oldPage); await DemoReady(vm);
        vm.RuleEditor.StartPreview(newPage); vm.RuleEditor.StopPreview(oldPage);
        vm.RuleEditor.Side = 180; await DemoReady(vm);
        Assert.Equal(180, vm.RuleEditor.Side); Assert.NotNull(vm.RuleEditor.PreviewLayout);
        await vm.RuleEditor.StopPreviewAsync(); await vm.FlushSettingsAsync();
    });
}
