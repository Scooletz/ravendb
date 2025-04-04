import { useMemo } from "react";

export type Browser = "Chrome" | "Firefox" | "Safari" | "Other";

export function useBrowser(): Browser {
    return useMemo(() => {
        const userAgent = window.navigator.userAgent.toLowerCase();

        // Check for Chromium-based browsers first
        if (userAgent.includes("chrome") || userAgent.includes("edg") || userAgent.includes("opr")) {
            return "Chrome";
        } else if (userAgent.includes("firefox")) {
            return "Firefox";
        } else if (userAgent.includes("safari") && !userAgent.includes("chrome")) {
            return "Safari";
        }

        return "Other";
    }, []);
}
