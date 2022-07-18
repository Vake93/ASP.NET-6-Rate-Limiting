// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using AspNetCore6.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RateLimitingSample;
using System;
using System.Linq;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<TodoDb>(opt => opt.UseInMemoryDatabase("TodoList"));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
// Inject an ILogger<SampleRateLimiterPolicy>
builder.Services.AddLogging();

var app = builder.Build();

var todoName = "todoPolicy";
var completeName = "completePolicy";
var helloName = "helloPolicy";

// Define endpoint limiters and a global limiter.
var options = new RateLimiterOptions()
        .AddTokenBucketLimiter(todoName, new TokenBucketRateLimiterOptions(1, QueueProcessingOrder.OldestFirst, 1, TimeSpan.FromSeconds(10), 1))
        .AddPolicy<string>(completeName, new SampleRateLimiterPolicy(NullLogger<SampleRateLimiterPolicy>.Instance))
        .AddPolicy<string, SampleRateLimiterPolicy>(helloName);
// The global limiter will be a concurrency limiter with a max permit count of 10 and a queue depth of 5.
options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        {
            return RateLimitPartition.CreateConcurrencyLimiter<string>("globalLimiter", key => new ConcurrencyLimiterOptions(10, QueueProcessingOrder.NewestFirst, 5));
        });
app.UseRateLimiter(options);

// The limiter on this endpoint allows 1 request every 5 seconds
app.MapGet("/", () => "Hello World!").RequireRateLimiting(helloName);

// Requests to this endpoint will be processed in 10 second intervals
app.MapGet("/todoitems", async (TodoDb db) =>
    await db.Todos.ToListAsync())
    .RequireRateLimiting(todoName);

// The limiter on this endpoint allows 1 request every 5 seconds
app.MapGet("/todoitems/complete", async (TodoDb db) =>
    await db.Todos.Where(t => t.IsComplete).ToListAsync())
    .RequireRateLimiting(completeName);

app.MapGet("/todoitems/{id}", async (int id, TodoDb db) =>
    await db.Todos.FindAsync(id)
        is Todo todo
            ? Results.Ok(todo)
            : Results.NotFound());

app.MapPost("/todoitems", async (Todo todo, TodoDb db) =>
{
    db.Todos.Add(todo);
    await db.SaveChangesAsync();

    return Results.Created($"/todoitems/{todo.Id}", todo);
});

app.MapPut("/todoitems/{id}", async (int id, Todo inputTodo, TodoDb db) =>
{
    var todo = await db.Todos.FindAsync(id);

    if (todo is null)
    {
        return Results.NotFound();
    }

    todo.Name = inputTodo.Name;
    todo.IsComplete = inputTodo.IsComplete;

    await db.SaveChangesAsync();

    return Results.NoContent();
});

app.MapDelete("/todoitems/{id}", async (int id, TodoDb db) =>
{
    if (await db.Todos.FindAsync(id) is Todo todo)
    {
        db.Todos.Remove(todo);
        await db.SaveChangesAsync();
        return Results.Ok(todo);
    }

    return Results.NotFound();
});

app.Run();

class Todo
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public bool IsComplete { get; set; }
}

class TodoDb : DbContext
{
    public TodoDb(DbContextOptions<TodoDb> options)
        : base(options) { }

    public DbSet<Todo> Todos => Set<Todo>();
}
