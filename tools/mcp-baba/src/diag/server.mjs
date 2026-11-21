#!/usr/bin/env node
// MCP baba-diag, entrada local (stdio). Ferramentas em fabrica.mjs; entrada remota em http.mjs (ADR-0069).
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { criarServidorDiag } from './fabrica.mjs';

await criarServidorDiag().connect(new StdioServerTransport());
