import { buildSeiProcessUrl, copySeiNumber, SEI_HOME_URL } from "../sei";

export function SeiLink({ number, processId, onCopy }) {
  const value = String(number ?? "").trim();
  if (!value && !processId) return null;

  if (processId) {
    return (
      <a
        className="sei-link"
        href={buildSeiProcessUrl(processId)}
        target="_blank"
        rel="noopener noreferrer"
        title="Abrir o processo no SEI Bahia"
        onClick={(event) => event.stopPropagation()}
      >
        {value || "Abrir processo no SEI"}
      </a>
    );
  }

  return (
    <a
      className="sei-link"
      href={SEI_HOME_URL}
      target="_blank"
      rel="noopener noreferrer"
      title="Abrir o SEI Bahia e copiar o número para a pesquisa rápida"
      onClick={(event) => {
        event.stopPropagation();
        copySeiNumber(value).then(
          () => onCopy?.(true),
          () => onCopy?.(false),
        );
      }}
    >
      {value}
    </a>
  );
}
