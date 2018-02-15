def limit_int(amount: int, high: int, low: int = 0) -> int:
    """
    Limits an integer.
    :param amount: amount
    :param high: high limit
    :param low: low limit
    :return: limited integer
    """
    return low if amount < low else (high if amount > high else amount)
