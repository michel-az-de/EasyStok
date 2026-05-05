// API Layer - Global usings para reduzir repetição em Controllers e Services
// Padrão: ASP.NET Core, Entity Framework, autorização, configuração, entidades

global using System;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using EasyStock.Api.Http;
global using EasyStock.Api.Services;
global using EasyStock.Application.Ports.Output;
global using EasyStock.Application.Ports.Output.Persistence;
global using EasyStock.Domain.Entities;
global using EasyStock.Domain.Enums;
global using EasyStock.Domain.Exceptions;
global using Microsoft.AspNetCore.Authorization;
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Logging;
