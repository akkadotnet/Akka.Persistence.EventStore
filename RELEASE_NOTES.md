#### 1.5.30 October 14, 2024 ####
Upgrade Akka to version 1.5.30
Upgrade EventStore to version 23.3.5
[Added a message adapter for System.Text.Json](https://github.com/akkadotnet/Akka.Persistence.EventStore/pull/54)
[Added support for a writer uuid](https://github.com/akkadotnet/Akka.Persistence.EventStore/pull/55)

#### 1.5.25 June 28 2024 ####
Upgrade Akka to version 1.5.26
[Fix for an issue](https://github.com/akkadotnet/Akka.Persistence.EventStore/pull/45) where tagged query would fail if some matching
events was deleted

#### 1.5.25 June 18 2024 ####
Upgrade Akka to version 1.5.25
Upgrade EventStore to version 23.3.3

#### 1.5.20 May 9 2024 ####
This is a patch release with some minor refactoring and updates to dependencies.

#### 1.5.18 April 8 2024 ####
Upgrade to Akka 1.5.18 dependency
Upgrade to EventStore 23.2.1 dependency

**Breaking Changes**
This is a complete rewrite of the library to use Akka.NET 1.5.18 and EventStore 23.2.1. The library is now net6.0+.
See README for more details.

#### 1.4.0 January 16 2019 ####
Serialization changes to match 1.4 API
Upgrade to Akka 1.4 dependency
Upgrade to EventStore 5 dependency
netstandard2 and netfx452 (from netstandard1.6 and netfx45)

**Breaking Changes**
EventAdapter interface change (see README)

#### 1.3.0 August 14 2017 ####
Initial version