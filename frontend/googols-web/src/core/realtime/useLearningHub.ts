import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { useEffect } from 'react'

/** Listens for server events (XP awards) and refreshes the affected queries. */
export function useLearningHub(enabled: boolean) {
  const queryClient = useQueryClient()

  useEffect(() => {
    if (!enabled) return
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/learning', { withCredentials: true })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('xpAwarded', () => {
      void queryClient.invalidateQueries({ queryKey: ['student', 'me'] })
      void queryClient.invalidateQueries({ queryKey: ['xp'] })
    })
    connection.start().catch(() => undefined) // realtime is a nicety; pages still work without it

    return () => { void connection.stop() }
  }, [enabled, queryClient])
}
