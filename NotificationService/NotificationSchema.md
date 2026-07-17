# Notification database contract

## Action types

| Value | GraphQL enum | Meaning |
| --- | --- | --- |
| `0` | `LIKE` | User A liked object X owned by User B. |
| `1` | `COMMENT` | User A commented on object X owned by User B. |
| `2` | `TAG` | User A tagged User B in object X. |
| `3` | `MENTION` | User A mentioned User B in object X. |
| `4` | `FRIEND_REQUEST` | User A sent User B a friend request. |
| `5` | `FRIEND_ACCEPT` | User A accepted User B's friend request. |
| `6` | `GROUP_INVITE` | User A invited User B to a group. |
| `7` | `GROUP_JOIN_REQUEST` | User A requested to join a group. |
| `8` | `GROUP_JOIN_ACCEPTED` | A group accepted User B. |
| `9` | `SHARE` | User A shared public content owned by User B. |

The Social Graph creates all ten variants through `POST /internal/notifications`.
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
    idempotency_key varchar(128) NOT NULL UNIQUE,
    realtime_published_at timestamptz NULL,
    publish_attempt_count integer NOT NULL DEFAULT 0,
    next_publish_attempt_at timestamptz NULL,
    last_publish_error varchar(2000) NULL
);

CREATE INDEX ix_notification_receiver_created_id
    ON notification (receiver_id, created_at DESC, id DESC);

CREATE INDEX ix_notification_unread_receiver
    ON notification (receiver_id)
    WHERE is_read = false;

CREATE INDEX ix_notification_pending_realtime
    ON notification (next_publish_attempt_at, created_at)
    WHERE realtime_published_at IS NULL;
```

Each notification row is also a durable realtime outbox item. The nullable
`realtime_published_at`, retry count/time and last-error columns are internal and are not
exposed through GraphQL. The dispatcher locks pending rows with `SKIP LOCKED`, publishes
to the receiver-specific topic, and retries failures with bounded exponential backoff.
Empty polling also backs off from `NotificationDelivery:PollMilliseconds` to
`NotificationDelivery:MaxIdlePollMilliseconds`, then resets as soon as work is found.
