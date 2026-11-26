import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterEach } from 'vitest'

afterEach(() => cleanup())

// jsdom has no layout engine; components call scrollIntoView to keep the chat at the bottom.
Element.prototype.scrollIntoView = function scrollIntoView() {}
