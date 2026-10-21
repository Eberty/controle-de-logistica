import { splitTags, tagColorKey, tagStyle } from "../tags";

export function TagList({ tags, colors = {} }) {
  const items = splitTags(tags);

  if (items.length === 0) return null;

  return (
    <div className="tag-list">
      {items.map((tag, index) => (
        <span key={`${tag}-${index}`} style={tagStyle(colors[tagColorKey(tag)])}>
          {tag}
        </span>
      ))}
    </div>
  );
}
