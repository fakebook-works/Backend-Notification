# Notification service agent rules

When embedded in the Fakebook workspace, also read the root API security contract.

- Derive the viewer from ICurrentGatewayUser after Gateway trust middleware.
- A caller may read/mark only their own notifications.
- Internal create/delete APIs require signed HMAC requests and Redis nonce replay protection.
- Never accept a browser-supplied recipient as proof of authorization.
- Preserve bounded pagination, SSE cancellation and delivered-notification retention.
- Runtime DB access uses the notification-scoped role; startup migrations stay disabled.
- Do not include secret headers, tokens or private content in notification logs/telemetry.

Run the NotificationService.Tests project and cover untrusted/wrong-user/replay cases.
