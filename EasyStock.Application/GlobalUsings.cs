// Application Layer - Global usings para reduzir repetição em UseCases e Validators
// Padrão: validação, logging, portas (repositórios), entidades de domínio, helpers

global using System;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using EasyStock.Application.Common;
global using EasyStock.Application.Ports.Output;
global using EasyStock.Application.Ports.Output.Persistence;
global using EasyStock.Application.UseCases.Common;
global using EasyStock.Domain.Entities;
global using EasyStock.Domain.Enums;
global using EasyStock.Domain.Exceptions;
global using Microsoft.Extensions.Logging;
