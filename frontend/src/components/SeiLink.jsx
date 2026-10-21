import { buildSeiSearchUrl, copySeiNumber } from "../sei";

export function SeiLink({ number }) {
  const value = String(number ?? "").trim();
  if (!value) return null;

  return (
    <a
      className="sei-link"
      href={buildSeiSearchUrl(value)}
      target="_blank"
      rel="noopener noreferrer"
      title="Pesquisar no SEI Bahia (o número também é copiado)"
      onClick={(event) => {
        event.stopPropagation();
        copySeiNumber(value);
      }}
    >
      {value}
    </a>
  );
}
