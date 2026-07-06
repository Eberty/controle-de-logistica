export function TagList({ tags }) {
  if (!tags) return null;

  const items = tags
    .split(",")
    .map((tag) => tag.trim())
    .filter(Boolean);

  if (items.length === 0) return null;

  return (
    <div className="tag-list">
      {items.map((tag, index) => (
        <span key={`${tag}-${index}`}>{tag}</span>
      ))}
    </div>
  );
}
