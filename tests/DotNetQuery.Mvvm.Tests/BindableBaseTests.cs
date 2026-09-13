namespace DotNetQuery.Mvvm.Tests;

public class BindableBaseTests
{
    private sealed class TestBindable : BindableBase
    {
        private int _value;
        private string? _name;

        public int Value
        {
            get => _value;
            set => SetProperty(ref _value, value);
        }

        public string? Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public bool SetValueWithExplicitName(int value, string propertyName) =>
            SetProperty(ref _value, value, propertyName);
    }

    [Test]
    public async Task SetProperty_WithChangedValue_UpdatesFieldAndReturnsTrue()
    {
        var sut = new TestBindable();

        var result = sut.SetValueWithExplicitName(42, nameof(TestBindable.Value));

        await Assert.That(result).IsTrue();
        await Assert.That(sut.Value).IsEqualTo(42);
    }

    [Test]
    public async Task SetProperty_WithChangedValue_RaisesPropertyChangedWithCallerMemberName()
    {
        var sut = new TestBindable();
        var raised = new List<string>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        sut.Value = 10;

        await Assert.That(raised).IsEquivalentTo([nameof(TestBindable.Value)]);
    }

    [Test]
    public async Task SetProperty_WithUnchangedValue_ReturnsFalseAndDoesNotRaise()
    {
        var sut = new TestBindable { Value = 5 };
        var raised = false;
        sut.PropertyChanged += (_, _) => raised = true;

        var result = sut.SetValueWithExplicitName(5, nameof(TestBindable.Value));

        await Assert.That(result).IsFalse();
        await Assert.That(raised).IsFalse();
    }

    [Test]
    public async Task SetProperty_WithReferenceTypeAndEqualValues_DoesNotRaise()
    {
        var sut = new TestBindable { Name = "same" };
        var raised = false;
        sut.PropertyChanged += (_, _) => raised = true;

        sut.Name = "same";

        await Assert.That(raised).IsFalse();
    }

    [Test]
    public async Task SetProperty_WithNullToNonNull_RaisesPropertyChanged()
    {
        var sut = new TestBindable();
        var raised = false;
        sut.PropertyChanged += (_, _) => raised = true;

        sut.Name = "value";

        await Assert.That(raised).IsTrue();
        await Assert.That(sut.Name).IsEqualTo("value");
    }

    [Test]
    public async Task SetProperty_WithExplicitPropertyName_UsesProvidedName()
    {
        var sut = new TestBindable();
        var raised = new List<string>();
        sut.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);

        sut.SetValueWithExplicitName(1, "CustomName");

        await Assert.That(raised).IsEquivalentTo(["CustomName"]);
    }
}
