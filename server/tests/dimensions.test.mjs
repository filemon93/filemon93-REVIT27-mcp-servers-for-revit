import { test } from 'node:test';
import assert from 'node:assert/strict';
import { z } from 'zod';
import { idSchema, dimensionSchema, guideDimensionSchema } from '../build/utils/dimensionSchemas.js';
import { Client } from '@modelcontextprotocol/sdk/client/index.js';
import { StdioClientTransport } from '@modelcontextprotocol/sdk/client/stdio.js';

test('dimension schema rejects invalid coordinates and unsafe numeric IDs', () => {
  assert.equal(idSchema.safeParse(Number.MAX_SAFE_INTEGER + 1).success, false);
  assert.equal(idSchema.parse('2147483648'), '2147483648');
  assert.equal(dimensionSchema.safeParse({startPoint:{x:Infinity,y:0,z:0},endPoint:{x:1,y:0,z:0}}).success, false);
  const guide = z.object(guideDimensionSchema);
  assert.equal(guide.safeParse({guideElementIds:[]}).success, false);
  assert.equal(guide.safeParse({guideElementIds:[1],minimumSegmentMm:-1}).success, false);
  assert.equal(guide.parse({guideElementIds:[1]}).wallReference, 'faces');
});

test('stdio server advertises all dimension tools with usable schemas', async () => {
  const client = new Client({name:'release-smoke-test',version:'1.0.0'});
  const transport = new StdioClientTransport({command:process.env.MCP_TEST_NODE || process.execPath,args:[process.env.MCP_TEST_ENTRY || 'build/index.js'],stderr:'pipe'});
  try {
    await client.connect(transport);
    const { tools } = await client.listTools();
    for (const name of ['create_dimensions','dimension_walls_by_guides','get_dimension_types']) {
      const tool = tools.find(t => t.name === name);
      assert.ok(tool, `Missing tool: ${name}`);
      assert.equal(tool.inputSchema.type, 'object');
    }
    assert.ok(tools.length >= 30, 'A tool import failed after dependency upgrade');
  } finally { await client.close(); }
});
