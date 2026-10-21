// SEI Bahia public search only accepts the protocol number by POST, so this page receives the
// number in its own URL (?numero=...) and submits the search form on load. That keeps the link in
// the app a plain GET link, which works in a new tab in any browser.
(function () {
  var SEI_PUBLIC_SEARCH_URL =
    "https://seibahia.ba.gov.br/sei/modulos/pesquisa/md_pesq_processo_pesquisar.php" +
    "?acao_externa=protocolo_pesquisar&acao_origem_externa=protocolo_pesquisar&id_orgao_acesso_externo=0";

  var number = (new URLSearchParams(window.location.search).get("numero") || "").trim().slice(0, 100);
  if (!number) {
    document.getElementById("sei-status").textContent = "Número do SEI não informado.";
    return;
  }

  var fields = {
    txtProtocoloPesquisa: number,
    chkSinProcessos: "P",
    chkSinDocumentosGerados: "G",
    chkSinDocumentosRecebidos: "R",
    hdnFlagPesquisa: "1",
  };

  var form = document.createElement("form");
  form.method = "POST";
  form.action = SEI_PUBLIC_SEARCH_URL;

  Object.keys(fields).forEach(function (name) {
    var input = document.createElement("input");
    input.type = "hidden";
    input.name = name;
    input.value = fields[name];
    form.appendChild(input);
  });

  document.body.appendChild(form);
  form.submit();
})();
