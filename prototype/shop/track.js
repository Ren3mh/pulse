// The shop's only way to send events. Every other script calls track().
// Builds a v1 event (docs/event-format.md) and posts it to the API.

const DISTINCT_ID_KEY = "pulse_distinct_id";

function distinctId() {
  // Anonymous id, kept per browser. Storage can be blocked, so fall back to a
  // per-page-load id instead of failing.
  try {
    let id = localStorage.getItem(DISTINCT_ID_KEY);
    if (!id) {
      id = "anon-" + crypto.randomUUID().slice(0, 8);
      localStorage.setItem(DISTINCT_ID_KEY, id);
    }
    return id;
  } catch {
    return (window.__pulseId ??= "anon-" + crypto.randomUUID().slice(0, 8));
  }
}

export async function track(eventName, properties = {}) {
  const event = {
    version: 1,
    event_id: crypto.randomUUID(), // client-generated, so a retry is not a duplicate
    event_name: eventName,
    distinct_id: distinctId(),
    occurred_at: new Date().toISOString(),
    properties,
  };
  try {
    const response = await fetch("/events", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(event),
      keepalive: true, // lets the request finish if the page is closing
    });
    if (!response.ok) console.warn("pulse: event rejected", eventName, await response.text());
  } catch (error) {
    // Analytics must never break the shop.
    console.warn("pulse: could not send event", eventName, error);
  }
  window.dispatchEvent(new CustomEvent("pulse:tracked", { detail: event }));
}
