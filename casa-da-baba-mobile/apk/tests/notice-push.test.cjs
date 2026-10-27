const {test, after} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), os = require('node:os'), path = require('node:path'), crypto = require('node:crypto');
const notice = require('../scripts/notice-push.cjs');
const sample = {id:'migration-20261026', title:'Traga seus dados', body:'Exporte o backup JSON e confira os registros antes de sincronizar.', screen:'migration-guide'};
const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'cdb-notice-test-'));
after(() => { const resolved=fs.realpathSync(directory); assert.ok(resolved.startsWith(fs.realpathSync(os.tmpdir())+path.sep) && path.basename(resolved).startsWith('cdb-notice-test-')); fs.rmSync(resolved,{recursive:true,force:true}); });
function options() {
  const credentials=path.join(directory,'credentials.json'), appConfig=path.join(directory,'app.json');
  const pair=crypto.generateKeyPairSync('rsa',{modulusLength:2048,privateKeyEncoding:{type:'pkcs8',format:'pem'},publicKeyEncoding:{type:'spki',format:'pem'}});
  fs.writeFileSync(credentials,JSON.stringify({type:'service_account',project_id:'cdb-test-project',client_email:'push@cdb-test-project.iam.gserviceaccount.com',private_key:pair.privateKey}));
  fs.writeFileSync(appConfig,JSON.stringify({project_info:{project_id:'cdb-test-project'},client:[{client_info:{android_client_info:{package_name:'com.casadababa.piloto'}}}]}));
  return {credentials,appConfig,notice:sample};
}
test('aviso visível usa tópico exclusivo, canal e rota fixa, sem alterar atualização silenciosa',()=>{
 const m=notice.message(sample); assert.equal(m.validate_only,true); assert.equal(m.message.topic,notice.TOPIC);
 assert.deepEqual(m.message.notification,{title:sample.title,body:sample.body});
 assert.deepEqual(m.message.data,{kind:'app-notice',noticeId:sample.id,screen:'migration-guide'});
 assert.equal(m.message.android.priority,'high'); assert.equal(m.message.android.notification.channel_id,'cdb-operacao');
 assert.equal(m.message.android.notification.tag,sample.id); assert.equal(m.message.android.notification.click_action,'com.casadababa.app.OPEN_NOTICE');
});
test('recusa URL externa, destinatário arbitrário, campos extras e texto fora dos limites',()=>{
 for(const delta of [{screen:'https://evil.test'},{id:'../a'},{title:''},{body:'x'.repeat(1201)},{title:'x'.repeat(101)},{token:'outro'},{body:5},{body:'界'.repeat(1200)}]) assert.throws(()=>notice.message({...sample,...delta}));
});
test('valida por padrão, envio explícito recebe aceite sem expor credenciais',async()=>{
 const o=options(); let sent;
 const transport=async(url,body)=>url.includes('oauth2')?{access_token:'test-token'}:(sent=JSON.parse(body),{name:'projects/test/messages/1'});
 assert.equal((await notice.send(o,transport)).status,'validated'); assert.equal(sent.validate_only,true);
 const result=await notice.send({...o,validateOnly:false},transport);assert.equal(result.status,'accepted');assert.equal(sent.validate_only,false);assert.ok(!JSON.stringify(result).includes('test-token'));
});
test('projeto divergente, falha OAuth e falta de aceite nunca retornam sucesso',async()=>{
 const o=options(); fs.writeFileSync(o.appConfig,JSON.stringify({project_info:{project_id:'outro'}}));
 await assert.rejects(notice.send(o,()=>{throw new Error('Não pode enviar');}),/Projeto/);
 const valid=options(); await assert.rejects(notice.send(valid,async()=>({access_token:'bad\nheader'})),/OAuth/);
 await assert.rejects(notice.send(valid,async(url)=>url.includes('oauth2')?{access_token:'ok'}:{}),/confirmou/);
 await assert.rejects(notice.send(valid,async()=>{throw new Error('rede indisponível');}),/rede/);
});
