// See https://aka.ms/new-console-template for more information

using Org.Grush.EchoWorkDisplay;

Console.WriteLine("Hello, World!");

await using var mediaManager = await GlobalMediaReader.InitAsync();

