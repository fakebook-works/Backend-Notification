# Notification database contract

## Action types

| Value | GraphQL enum | Meaning |
| --- | --- | --- |
| `0` | `LIKE` | User A liked object X owned by User B. |
| `1` | `COMMENT` | User A commented on object X owned by User B. |
| `2` | `TAG` | User A tagged User B in object X. |
| `3` | `MENTION` | User A mentioned User B in object X. |

The Social Graph creates all four variants through `POST /internal/notifications`.
It supplies `creatorId`, `receiverId`, `actionType`, `objectId`, and an
`Idempotency-Key` header. The service creates the Snowflake `id` itself.

`receiver_id` is intentionally spelled correctly; do not introduce the former
`reciver_id` typo. `data` is not stored for this version.

## Read behavior

- `markNotificationRead(id)` marks one notification as read only for its receiver.
- `markAllNotificationsRead()` marks all unread notifications for the trusted Gateway user.
- Feed queries always filter by the trusted Gateway user (`receiver_id`); callers cannot supply another user ID.

## PostgreSQL schema

```sql
CREATE TABLE notification (
    id bigint PRIMARY KEY,
    creator_id bigint NOT NULL,
    receiver_id bigint NOT NULL,
    action_type smallint NOT NULL,
    object_id bigint NOT NULL,
    created_at timestamptz NOT NULL DEFAULT CURRENT_TIMESTAMP,
    is_read boolean NOT NULL DEFAULT false,
    idempotency_key varchar(128) NOT NULL UNIQUE
);

CREATE INDEX ix_notification_receiver_created_id
    ON notification (receiver_id, created_at DESC, id DESC);

CREATE INDEX ix_notification_unread_receiver
    ON notification (receiver_id)
    WHERE is_read = false;
```
