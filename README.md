# Enhanced.NRedisStack

## Overview

Enhanced.NRedisStack is a C# library that provides an efficient way to interact with Redis data structures. It allows developers to define their data models using C# classes and generate JSON schemas for Redis.

## Features

- Define data models using C# classes.
- Generate JSON schemas for Redis data structures.
- Use custom attributes to specify how properties should be treated when generating the Redis schema.

## Example

Here's a simple example of how to use Enhanced.NRedisStack:

```csharp
// Define your data model
public class Account
{
    [RedisNumeric]
    public decimal Balance { get; set; } = 0;
    
    [RedisObject(AliasPrefix = "address")]
    public Address Address { get; set; } = new Address();
}

public class Address
{
    [RedisText(Alias = "street")]
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Zip { get; set; } = string.Empty;
}

// Generate the schema
Schema schema = Schemas.GetAccountSchema();
```

## Installation

Provide instructions on how to install and use your library.

## Documentation

Link to your full documentation here.

## Contributing

Provide instructions on how to contribute to your project.

## License

MIT License