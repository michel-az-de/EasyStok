// Global usings do Admin.
//
// Cada `global using` aqui torna o namespace disponível em TODOS os arquivos
// do projeto sem precisar repetir o `using` no topo.
//
// Critérios pra adicionar:
//   - System básicos: simetria com EasyStock.Api/GlobalUsings.cs (redundantes
//     com ImplicitUsings mas explicitados aqui pra consistência entre projetos).
//   - Namespaces de aplicação: aparecia em >50% dos 67 arquivos .cs do projeto
//     no levantamento da F0a (Mvc 61%, System.Text.Json 60%, Admin.Services 55%).
//
// Conservador de propósito — globalizar tipos com nomes muito comuns pode
// criar ambiguidade silenciosa.
//
// F1 (dotnet format --diagnostics=IDE0005) limpa os usings locais redundantes
// resultantes desta adição.

global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading;
global using System.Threading.Tasks;
global using System.Text.Json;

global using EasyStock.Admin.Services;

global using Microsoft.AspNetCore.Mvc;
