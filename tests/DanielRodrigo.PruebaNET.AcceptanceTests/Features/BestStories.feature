Feature: Best stories
  As a client of the service
  I want the best Hacker News stories, highest score first
  So that I get them without the service overloading Hacker News

  Scenario: The best stories are returned in descending order of score
    Given Hacker News lists the following best stories:
      | title                                | score |
      | Show HN: A terminal emulator in Rust | 180   |
      | PostgreSQL 18 released               | 452   |
      | The case against microservices       | 297   |
    When a client asks for the best 2 stories
    Then it receives, in this order:
      | title                          | score |
      | PostgreSQL 18 released         | 452   |
      | The case against microservices | 297   |

  Scenario: A story without a link is returned without one
    Given Hacker News lists the best story "Ask HN: Who is hiring? (September 2026)" which has no link
    When a client asks for the best 1 story
    Then it receives "Ask HN: Who is hiring? (September 2026)" without a link

  Scenario: Hacker News is asked for the stories once no matter how many clients ask
    Given Hacker News lists 25 best stories
    And Hacker News is slow to answer
    When 300 clients ask for the best 10 stories at the same time
    And Hacker News answers
    Then every client receives the same 10 stories
    And Hacker News was asked for the list of best stories once

  Scenario: Hacker News is not asked again once the stories are loaded, no matter how many clients ask
    Given the best stories have been loaded
    When 300 clients ask for the best 5 stories at the same time
    Then every client receives the same 5 stories
    And Hacker News was not asked again

  Scenario: Stories keep being served while Hacker News is unavailable
    Given the best stories have been loaded
    And Hacker News stops responding
    When 3 refresh intervals go by
    Then clients still receive the stories
    And the service reports that its data is stale

  Scenario Outline: Requests for an invalid number of stories are rejected
    When a client asks for the best <count> stories
    Then the request is rejected as invalid

    Examples:
      | count |
      | 0     |
      | 501   |
