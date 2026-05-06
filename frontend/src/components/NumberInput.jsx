export function NumberInput({ value, min = 0, max, required = false, disabled = false, onValueChange }) {
  const hasMax = Number.isFinite(Number(max));
  const displayedValue = disabled ? "0" : value;

  function clampValue(numericValue) {
    const minValue = Number(min);
    const nextValue = Math.max(minValue, numericValue);
    return hasMax ? Math.min(Number(max), nextValue) : nextValue;
  }

  function sanitizeNumber(rawValue) {
    const digitsOnly = rawValue.replace(/\D/g, "");
    if (!digitsOnly) return "";

    const numericValue = Number(digitsOnly);
    if (!Number.isFinite(numericValue)) return "";

    return String(clampValue(numericValue));
  }

  function stepValue(direction) {
    const numericValue = Number(value);
    const baseValue = Number.isFinite(numericValue) ? numericValue : min;
    const nextValue = clampValue(baseValue + direction);
    onValueChange(String(nextValue));
  }

  function handleKeyDown(event) {
    if (event.key === "ArrowUp") {
      event.preventDefault();
      stepValue(1);
    }

    if (event.key === "ArrowDown") {
      event.preventDefault();
      stepValue(-1);
    }
  }

  return (
    <div className={`number-field${disabled ? " is-disabled" : ""}`}>
      <input
        type="text"
        inputMode="numeric"
        pattern="[0-9]*"
        value={displayedValue}
        disabled={disabled}
        onChange={(event) => onValueChange(sanitizeNumber(event.target.value))}
        onKeyDown={handleKeyDown}
        onPaste={(event) => {
          event.preventDefault();
          onValueChange(sanitizeNumber(event.clipboardData.getData("text")));
        }}
        required={required}
      />
      <div className="number-stepper">
        <button
          type="button"
          className="step-up"
          aria-label="Aumentar quantidade"
          disabled={disabled || (hasMax && Number(value) >= Number(max))}
          onClick={() => stepValue(1)}
        />
        <button
          type="button"
          className="step-down"
          aria-label="Diminuir quantidade"
          disabled={disabled || Number(value) <= Number(min)}
          onClick={() => stepValue(-1)}
        />
      </div>
    </div>
  );
}
