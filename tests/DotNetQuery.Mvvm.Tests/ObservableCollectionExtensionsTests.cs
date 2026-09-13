using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace DotNetQuery.Mvvm.Tests;

public class ObservableCollectionExtensionsTests
{
    private sealed record Item(int Id, string Name);

    private sealed record SourceModel(int Id, string Title);

    private sealed class RowViewModel(int id, string title) : BindableBase
    {
        private string _title = title;

        public int Id { get; } = id;

        public string Title
        {
            get => _title;
            set => SetProperty(ref _title, value);
        }
    }

    private static List<NotifyCollectionChangedAction> RecordActions(ObservableCollection<Item> target)
    {
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => actions.Add(e.Action);

        return actions;
    }

    [Test]
    public async Task SyncFrom_SameSequence_RaisesNoEvents()
    {
        var target = new ObservableCollection<Item> { new(1, "A"), new(2, "B") };
        var actions = RecordActions(target);

        target.SyncFrom([new(1, "A"), new(2, "B")], i => i.Id);

        await Assert.That(actions).IsEmpty();
    }

    [Test]
    public async Task SyncFrom_NewKey_RaisesSingleAddEvent()
    {
        var target = new ObservableCollection<Item> { new(1, "A") };
        var actions = RecordActions(target);

        target.SyncFrom([new(1, "A"), new(2, "B")], i => i.Id);

        await Assert.That(actions).IsEquivalentTo([NotifyCollectionChangedAction.Add]);
        await Assert.That(target.Select(i => i.Id)).IsEquivalentTo(new[] { 1, 2 });
    }

    [Test]
    public async Task SyncFrom_RemovedKey_RaisesSingleRemoveEvent()
    {
        var target = new ObservableCollection<Item> { new(1, "A"), new(2, "B") };
        var actions = RecordActions(target);

        target.SyncFrom([new(1, "A")], i => i.Id);

        await Assert.That(actions).IsEquivalentTo([NotifyCollectionChangedAction.Remove]);
        await Assert.That(target.Select(i => i.Id)).IsEquivalentTo(new[] { 1 });
    }

    [Test]
    public async Task SyncFrom_Reorder_RaisesSingleMoveEvent()
    {
        var target = new ObservableCollection<Item> { new(1, "A"), new(2, "B"), new(3, "C") };
        var actions = RecordActions(target);

        // A rotation: every algorithm that only moves what's needed settles this in one Move.
        target.SyncFrom([new(3, "C"), new(1, "A"), new(2, "B")], i => i.Id);

        await Assert.That(actions).IsEquivalentTo([NotifyCollectionChangedAction.Move]);
        await Assert.That(target.Select(i => i.Id)).IsEquivalentTo(new[] { 3, 1, 2 });
    }

    [Test]
    public async Task SyncFrom_ChangedValueAtSameKey_RaisesReplace()
    {
        var target = new ObservableCollection<Item> { new(1, "A") };
        var actions = RecordActions(target);

        target.SyncFrom([new(1, "A-renamed")], i => i.Id);

        await Assert.That(actions).IsEquivalentTo([NotifyCollectionChangedAction.Replace]);
        await Assert.That(target[0].Name).IsEqualTo("A-renamed");
    }

    [Test]
    public async Task SyncFrom_UnchangedValueAtSameKey_RaisesNoEvent()
    {
        var target = new ObservableCollection<Item> { new(1, "A") };
        var actions = RecordActions(target);

        // A new instance, but equal by record equality -- the default item comparer.
        target.SyncFrom([new Item(1, "A")], i => i.Id);

        await Assert.That(actions).IsEmpty();
    }

    [Test]
    public async Task SyncFrom_DuplicateSourceKeys_ThrowsArgumentException()
    {
        var target = new ObservableCollection<Item>();
        var act = () => target.SyncFrom([new(1, "A"), new(1, "B")], i => i.Id);

        var ex = await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentException>();
        await Assert.That(ex?.ParamName).IsEqualTo("source");
    }

    [Test]
    public async Task SyncFrom_Projection_NewSource_InsertsCreatedRow()
    {
        var target = new ObservableCollection<RowViewModel>();

        target.SyncFrom([new SourceModel(1, "A")], s => s.Id, r => r.Id, s => new RowViewModel(s.Id, s.Title));

        await Assert.That(target.Count).IsEqualTo(1);
        await Assert.That(target[0].Title).IsEqualTo("A");
    }

    [Test]
    public async Task SyncFrom_Projection_RemovedSource_RemovesRow()
    {
        var target = new ObservableCollection<RowViewModel> { new(1, "A"), new(2, "B") };

        target.SyncFrom([new SourceModel(1, "A")], s => s.Id, r => r.Id, s => new RowViewModel(s.Id, s.Title));

        await Assert.That(target.Select(r => r.Id)).IsEquivalentTo(new[] { 1 });
    }

    [Test]
    public async Task SyncFrom_Projection_MatchedRowWithUpdate_NeverRaisesReplace()
    {
        var row = new RowViewModel(1, "A");
        var target = new ObservableCollection<RowViewModel> { row };
        var actions = new List<NotifyCollectionChangedAction>();
        target.CollectionChanged += (_, e) => actions.Add(e.Action);

        target.SyncFrom(
            [new SourceModel(1, "A-renamed")],
            s => s.Id,
            r => r.Id,
            s => new RowViewModel(s.Id, s.Title),
            (r, s) => r.Title = s.Title
        );

        // The row is refreshed in place -- never replaced, so a bound selection on it survives.
        await Assert.That(actions).DoesNotContain(NotifyCollectionChangedAction.Replace);
        await Assert.That(target[0]).IsSameReferenceAs(row);
        await Assert.That(target[0].Title).IsEqualTo("A-renamed");
    }

    [Test]
    public async Task SyncFrom_Projection_MatchedRowWithoutUpdate_LeavesRowUntouched()
    {
        var row = new RowViewModel(1, "A");
        var target = new ObservableCollection<RowViewModel> { row };

        target.SyncFrom([new SourceModel(1, "A-renamed")], s => s.Id, r => r.Id, s => new RowViewModel(s.Id, s.Title));

        await Assert.That(target[0]).IsSameReferenceAs(row);
        // No update delegate was supplied, so the row's own data is left as it was.
        await Assert.That(target[0].Title).IsEqualTo("A");
    }

    [Test]
    public async Task SyncFrom_Projection_RefetchWithEqualData_PreservesSelectedInstance()
    {
        var target = new ObservableCollection<RowViewModel>();
        Sync(target, [new SourceModel(1, "A"), new SourceModel(2, "B")]);
        var selected = target[1];

        // A refetch that returns data equal in value but as brand-new SourceModel instances --
        // the scenario a query re-fetch produces.
        Sync(target, [new SourceModel(1, "A"), new SourceModel(2, "B")]);

        await Assert.That(target[1]).IsSameReferenceAs(selected);

        static void Sync(ObservableCollection<RowViewModel> target, IEnumerable<SourceModel> source) =>
            target.SyncFrom(
                source,
                s => s.Id,
                r => r.Id,
                s => new RowViewModel(s.Id, s.Title),
                (r, s) => r.Title = s.Title
            );
    }

    [Test]
    public async Task SyncFrom_Projection_DuplicateSourceKeys_ThrowsArgumentException()
    {
        var target = new ObservableCollection<RowViewModel>();
        var act = () =>
            target.SyncFrom(
                [new SourceModel(1, "A"), new SourceModel(1, "B")],
                s => s.Id,
                r => r.Id,
                s => new RowViewModel(s.Id, s.Title)
            );

        var ex = await Assert.That(act).ThrowsException().And.IsTypeOf<ArgumentException>();
        await Assert.That(ex?.ParamName).IsEqualTo("source");
    }
}
