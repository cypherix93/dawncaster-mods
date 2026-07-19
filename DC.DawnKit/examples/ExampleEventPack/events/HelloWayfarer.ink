// Hello Wayfarer — the smallest opportunity event: text, a speaker tag,
// two choices, one dialogue action, END. Compile with inklecate v1.0.0
// (emits inkVersion 20; ink >= 1.1 emits 21, which the game refuses).
A weary wayfarer waves at you from across the road. #A Wayfarer
* [Wave back]
    The wayfarer smiles and tosses you a small pouch of coins. #A Wayfarer
    >>>>gold:50
    -> END
* [Walk on]
    You keep to your side of the road and walk on. #A Wayfarer
    -> END
