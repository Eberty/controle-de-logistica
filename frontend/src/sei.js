// SEI rejects links to a process built outside a logged-in session ("Link sem assinatura"). With the internal process
// id from the "acesso direto" link, the process opens directly; with only the number, the link opens the SEI entry
// page and the number is copied to be pasted into the SEI quick search, which opens the process.
export const SEI_HOME_URL = "https://seibahia.ba.gov.br/sei/";

export function buildSeiProcessUrl(processId) {
  return `${SEI_HOME_URL}controlador.php?acao=procedimento_trabalhar&id_procedimento=${encodeURIComponent(processId)}`;
}

export function copySeiNumber(number) {
  return navigator.clipboard?.writeText(String(number ?? "").trim()) ?? Promise.reject(new Error("Sem área de transferência"));
}
