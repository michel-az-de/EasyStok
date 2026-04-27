// Azure Function que retorna o JSON de update pro plugin
// @capgo/capacitor-updater. O plugin faz POST e SWA static file
// retorna 405; aqui a function aceita GET ou POST igual.
//
// O workflow do SWA substitui os placeholders abaixo a cada deploy:
//   __VERSION__   -> "1.0.<run_number>"
//   __URL__       -> URL completa do bundle-X.zip
//   __CHECKSUM__  -> SHA-256 do bundle.zip
module.exports = async function (context, req) {
  context.res = {
    status: 200,
    headers: { "Content-Type": "application/json" },
    body: {
      version: "__VERSION__",
      url: "__URL__",
      checksum: "__CHECKSUM__"
    }
  };
};
