# Akka.Persistence.EventStore Benchmarks

This benchmark uses BenchmarkDotNet to benchmark the performance of `CurrentEventsByTag` query.

How to run this benchmark:
1. You have to have docker installed on your machine.
2. Go to the project directory.
3. Seed data by running `dotnet run seed -c Release`
4. Run the benchmark by running `dotnet run -c Release`
5. Cleanup data by running `dotnet run cleanup -c Release`