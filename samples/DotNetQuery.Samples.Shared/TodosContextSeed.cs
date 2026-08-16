using Microsoft.Extensions.DependencyInjection;

namespace DotNetQuery.Samples.Shared;

public static class TodosContextSeed
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<ITodosClient>();

        var lists = await client.GetTodoListsAsync();
        if (lists.Count > 0)
        {
            return;
        }

        var groceries = await client.CreateTodoListAsync("Groceries");
        await client.AddTodoItemAsync(groceries.Id, "Milk");
        await client.AddTodoItemAsync(groceries.Id, "Eggs");
        await client.AddTodoItemAsync(groceries.Id, "Bread");
        await client.AddTodoItemAsync(groceries.Id, "Butter");
        await client.ToggleTodoItemAsync((await client.GetTodoItemsAsync(groceries.Id))[0].Id);

        var work = await client.CreateTodoListAsync("Work");
        await client.AddTodoItemAsync(work.Id, "Review pull requests");
        await client.AddTodoItemAsync(work.Id, "Write unit tests");
        await client.AddTodoItemAsync(work.Id, "Update documentation");
        await client.AddTodoItemAsync(work.Id, "Deploy to staging");

        var personal = await client.CreateTodoListAsync("Personal");
        await client.AddTodoItemAsync(personal.Id, "Book dentist appointment");
        await client.AddTodoItemAsync(personal.Id, "Call mom");
        await client.AddTodoItemAsync(personal.Id, "Renew gym membership");
        await client.ToggleTodoItemAsync((await client.GetTodoItemsAsync(personal.Id))[1].Id);

        var shopping = await client.CreateTodoListAsync("Shopping (long list)");
        string[] shoppingItems =
        [
            "Apples",
            "Bananas",
            "Oranges",
            "Grapes",
            "Strawberries",
            "Blueberries",
            "Watermelon",
            "Pineapple",
            "Mango",
            "Avocado",
            "Chicken breast",
            "Ground beef",
            "Salmon fillet",
            "Shrimp",
            "Bacon",
            "Cheddar cheese",
            "Mozzarella",
            "Greek yogurt",
            "Sour cream",
            "Heavy cream",
            "Pasta",
            "Rice",
            "Quinoa",
            "Bread",
            "Bagels",
            "Olive oil",
            "Soy sauce",
            "Hot sauce",
            "Ketchup",
            "Mustard",
        ];

        foreach (var item in shoppingItems)
        {
            await client.AddTodoItemAsync(shopping.Id, item);
        }
    }
}
