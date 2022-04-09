using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RevitApiTechChallenge.Services;
using RevitApiTechChallenge.Services.Impl;
using Serilog;
using Spectre.Console;

namespace RevitApiTechChallenge
{
    internal class Program
    {
        static async Task Main(string[] args)
        {

            var host = AppStartup();

            var service = ActivatorUtilities.GetServiceOrCreateInstance<IForgeService>(host.Services);

            var files = new List<string>();
            bool doneAddingFiles = false;
            while (!doneAddingFiles)
            {
                var file = AnsiConsole.Ask<string>("Insert file path:");
                if (!string.IsNullOrWhiteSpace(file))
                {
                    files.Add(file);

                    if (!AnsiConsole.Confirm("Add more files?"))
                    {
                        doneAddingFiles = true;
                    }
                }
                else
                {
                    if (!AnsiConsole.Confirm("Invalid string, try again?"))
                    {
                        doneAddingFiles = true;
                    }
                }
            }

            var v = AnsiConsole.Prompt(
                new TextPrompt<int>("Insert the target version:")
                .PromptStyle("green")
                .ValidationErrorMessage("[red]invalid input")
                .Validate(ver =>
                {
                    return ver switch
                    {
                        < 2009 => ValidationResult.Error("[red] version too low"),
                        > 2022 => ValidationResult.Error("[red] version too high"),
                        _ => ValidationResult.Success()
                    };
                }));


            var url = AnsiConsole.Ask<string>("Insert the target url: ");

            AnsiConsole.WriteLine("Starting processing...");
            var result = await service.TriggerJob(files.ToArray(), v.ToString(), url);

            if (!result.Success)
            {
                AnsiConsole.WriteLine("There was an error:");
                AnsiConsole.WriteLine(result.Error);
            }

            
        }

        static IHost AppStartup()
        {
            var builder = new ConfigurationBuilder();
            BuildConfig(builder);

            // Specifying the configuration for serilog
            Log.Logger = new LoggerConfiguration() // initiate the logger configuration
                            .ReadFrom.Configuration(builder.Build()) // connect serilog to our configuration folder
                            .Enrich.FromLogContext() //Adds more information to our logs from built in Serilog 
                            .WriteTo.Console() // decide where the logs are going to be shown
                            .CreateLogger(); //initialise the logger

            Log.Logger.Information("Application Starting");

            var host = Host.CreateDefaultBuilder() // Initialising the Host 
                        .ConfigureServices((context, services) => { // Adding the DI container for configuration
                            services.AddTransient<IForgeService, ForgeService>();
                        })
                        .UseSerilog() // Add Serilog
                        .Build(); // Build the Host

            
            return host;
        }


        static void BuildConfig(IConfigurationBuilder builder)
        {
            // Check the current directory that the application is running on 
            // Then once the file 'appsetting.json' is found, we are adding it.
            // We add env variables, which can override the configs in appsettings.json
            builder.SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                //.AddJsonFile("forgesettings.json", optional: false, reloadOnChange: true)
                .AddEnvironmentVariables();
        }


      


    }
}
