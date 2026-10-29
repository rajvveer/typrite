import { useEffect, useState } from "react";

export const ROUTES = ["login", "devices", "campaigns", "releases", "reports", "audit"] as const;
export type Route = (typeof ROUTES)[number];

export const DEFAULT_ROUTE: Route = "devices";

function isRoute(value: string): value is Route {
  return (ROUTES as readonly string[]).includes(value);
}

export function parseHash(hash: string): Route {
  const path = hash.replace(/^#\/?/, "").split(/[?/]/)[0] ?? "";
  return isRoute(path) ? path : DEFAULT_ROUTE;
}

export function currentRoute(): Route {
  return parseHash(window.location.hash);
}

export function navigate(route: Route): void {
  const next = `#/${route}`;
  if (window.location.hash === next) return;
  window.location.hash = next;
}

export function useRoute(): Route {
  const [route, setRoute] = useState<Route>(currentRoute);
  useEffect(() => {
    const onChange = (): void => setRoute(currentRoute());
    window.addEventListener("hashchange", onChange);
    return () => window.removeEventListener("hashchange", onChange);
  }, []);
  return route;
}
