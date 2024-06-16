using Domain.Bus;
using Infrastructure.IoC;
using Microsoft.Extensions.Configuration;
using Web.Controllers.Conifg;
using Web.Events;
using Web.Handlers;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers()
                .ConfigureApiBehaviorOptions(options =>
                {
                    options.InvalidModelStateResponseFactory = ModelStateResponses.ProduceErrorResponse;
                });

var rabbitMQSection = builder.Configuration.GetSection("RabbitMQ");
builder.Services.AddRabbitMQEventBus
(
    connectionUrl: rabbitMQSection["ConnectionUrl"],
    brokerName: "netCoreEventBusBroker",
    queueName: "netCoreEventBusQueue",
    timeoutBeforeReconnecting: 10
);
builder.Services.AddTransient<MessageSentEventHandler>();

// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.Run();

ConfigureEventBusHandlers(app);



void ConfigureEventBusHandlers(IApplicationBuilder app)
{
    var eventBus = app.ApplicationServices.GetRequiredService<IEventBus>();

    // Here you add the event handlers for each intergration event.
    eventBus.Subscribe<MessageSentEvent, MessageSentEventHandler>();
}