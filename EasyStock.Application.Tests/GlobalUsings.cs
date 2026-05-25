// Application Tests - Global usings para reduzir repetição em testes
// Padrão: test frameworks, assertions, mocks, tipos do domínio

global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using EasyStock.Application.UseCases.Common;
global using EasyStock.Domain.Entities;
global using EasyStock.Domain.Enums;
global using EasyStock.Domain.Exceptions;
global using FluentAssertions;
global using NSubstitute;
global using Xunit;
