// See https://aka.ms/new-console-template for more information

using NDFParser;
using NDFParser.AST;
using System.Diagnostics;

var origFile = args[0];

var newFile = args[1];
var stopwatch = Stopwatch.StartNew();
var ast = Parser.ParseFromFile(origFile) as IASTNode;

System.Console.WriteLine("Great Success!");

using (var file = File.CreateText(newFile))
{
    ast.Accept(new Writer(file));
}

stopwatch.Stop();

Console.WriteLine($"Elapsed Time: {stopwatch.ElapsedMilliseconds} ms");